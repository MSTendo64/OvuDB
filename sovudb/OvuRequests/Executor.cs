using System.Buffers;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;
using ovudb.Core;
using ovudb.OvuRequests.Ast;
using ovudb.Query;
using ovudb.SystemDatabase;

namespace ovudb.OvuRequests;

/// <summary>
/// ovuRequests query executor. Executes optimized queries and returns results.
/// </summary>
public unsafe class Executor
{
    private readonly Database _database;
    private readonly ModelService? _modelService;
    
    // Column lookup cache
    private readonly Dictionary<string, FrozenDictionary<string, string>> _columnCache = new();
    private readonly object _cacheLock = new();

    public Executor(Database database, ModelService? modelService = null)
    {
        _database = database;
        _modelService = modelService;
    }

    /// <summary>
    /// Execute query
    /// </summary>
    public object Execute(QueryNode query)
    {
        return query switch
        {
            SelectNode select => ExecuteSelect(select),
            InsertNode insert => ExecuteInsert(insert),
            UpdateNode update => ExecuteUpdate(update),
            DeleteNode delete => ExecuteDelete(delete),
            CreateTableNode createTable => ExecuteCreateTable(createTable),
            DropTableNode dropTable => ExecuteDropTable(dropTable),
            ModelAddNode modelAdd => ExecuteModelAdd(modelAdd),
            ModelListNode modelList => ExecuteModelList(modelList),
            ModelEditNode modelEdit => ExecuteModelEdit(modelEdit),
            ModelSeeNode modelSee => ExecuteModelSee(modelSee),
            ModelDelNode modelDel => ExecuteModelDel(modelDel),
            ModelUseNode modelUse => ExecuteModelUse(modelUse),
            _ => throw new NotSupportedException($"Unsupported query type: {query.GetType().Name}")
        };
    }

    /// <summary>
    /// Execute SELECT query
    /// </summary>
    private object ExecuteSelect(SelectNode node)
    {
        if (!_database.TableExists(node.TableName))
        {
            throw new InvalidOperationException($"Table {node.TableName} not found");
        }

        // Validate columns exist (except * and aggregate functions)
        if (!node.Columns.Contains("*") && node.Columns.Count > 0)
        {
            // Load table schema for column check
            var storage = _database.GetStorage();
            var loaded = storage.LoadTable(node.TableName);
            if (loaded.HasValue)
            {
                var (schema, _) = loaded.Value;
                var schemaColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                
                // Get column list from schema
                if (schema.TryGetValue("Columns", out var columnsObj) && columnsObj is List<object> columnsList)
                {
                    foreach (var colObj in columnsList)
                    {
                        Dictionary<string, object>? colDict = null;
                        if (colObj is Dictionary<string, object> dict)
                        {
                            colDict = dict;
                        }
                        else
                        {
                            try
                            {
                                var json = System.Text.Json.JsonSerializer.Serialize(colObj);
                                colDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                            }
                            catch
                            {
                                continue;
                            }
                        }
                        
                        if (colDict != null && colDict.TryGetValue("Name", out var nameObj))
                        {
                            schemaColumns.Add(nameObj?.ToString() ?? string.Empty);
                        }
                    }
                }
                
                // If schema empty, try to get columns from data
                if (schemaColumns.Count == 0)
                {
                    var tableData = LoadTableData(node.TableName);
                    if (tableData.Count > 0)
                    {
                        foreach (var key in tableData[0].Keys)
                        {
                            schemaColumns.Add(key);
                        }
                    }
                }
                
                // Check each column
                foreach (var column in node.Columns)
                {
                    // Skip aggregate functions (handled separately)
                    if (!node.AggregateFunctions.Any(agg => agg.ColumnName == column || agg.ColumnName == "*"))
                    {
                        if (!schemaColumns.Contains(column))
                        {
                            throw new InvalidOperationException($"Column '{column}' does not exist in table {node.TableName}");
                        }
                    }
                }
            }
        }

        // Execute query
        var results = ExecuteSelectInternal(node);
        
        return new Dictionary<string, object>
        {
            ["columns"] = node.Columns,
            ["rows"] = results,
            ["count"] = results.Count
        };
    }

    /// <summary>
    /// Internal SELECT execution
    /// </summary>
    private List<Dictionary<string, object?>> ExecuteSelectInternal(SelectNode node)
    {
        // Load table data
        var tableData = LoadTableData(node.TableName);
        if (tableData.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        // Get column cache for this table
        var columnMap = GetColumnMap(tableData[0], node.Columns);

        // Apply WHERE conditions
        if (node.Where != null)
        {
            tableData = ApplyWhereConditionsOptimized(tableData, node.Where.Condition, columnMap);
        }

        // If aggregate functions without GROUP BY, compute before LIMIT/OFFSET and return single row
        if (node.AggregateFunctions.Count > 0 && node.GroupBy == null)
        {
            var resultRow = new Dictionary<string, object?>();
            
            // Compute aggregates on all data (before LIMIT/OFFSET)
            foreach (var agg in node.AggregateFunctions)
            {
                var key = agg.Alias ?? $"{agg.Type}({agg.ColumnName})";
                resultRow[key] = CalculateAggregate(tableData, agg);
            }
            
            // If regular columns (not only aggregates), add from first row
            if (node.Columns.Count > 0 && !node.Columns.Contains("*") && tableData.Count > 0)
            {
                var firstRow = tableData[0];
                foreach (var column in node.Columns)
                {
                    if (columnMap.TryGetValue(column, out var actualKey) && firstRow.TryGetValue(actualKey, out var value))
                    {
                        resultRow[column] = value;
                    }
                }
            }
            
            return new List<Dictionary<string, object?>> { resultRow };
        }

        // Apply GROUP BY
        if (node.GroupBy != null)
        {
            tableData = ApplyGroupByOptimized(tableData, node.GroupBy, node.AggregateFunctions, columnMap);
        }

        // Apply ORDER BY
        if (node.OrderBy != null)
        {
            tableData = ApplyOrderByOptimized(tableData, node.OrderBy, columnMap);
        }

        // Apply OFFSET and LIMIT
        var startIndex = node.Offset?.Count ?? 0;
        var takeCount = node.Limit?.Count ?? tableData.Count;
        var endIndex = Math.Min(startIndex + takeCount, tableData.Count);

        // Select columns for each row
        var results = new List<Dictionary<string, object?>>(Math.Min(takeCount, tableData.Count));
        var selectAll = node.Columns.Contains("*");

        for (int i = startIndex; i < endIndex; i++)
        {
            var row = tableData[i];
            var resultRow = new Dictionary<string, object?>(selectAll ? row.Count : node.Columns.Count);
            
            if (selectAll)
            {
                foreach (var kvp in row)
                {
                    resultRow[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                foreach (var column in node.Columns)
                {
                    bool found = false;
                    if (columnMap.TryGetValue(column, out var actualKey) && row.TryGetValue(actualKey, out var value))
                    {
                        // Use query column name for client compatibility, value from actualKey
                        resultRow[column] = value;
                        found = true;
                    }
                    else
                    {
                        // Fallback: try direct lookup
                        foreach (var key in row.Keys)
                        {
                            if (string.Equals(key, column, StringComparison.OrdinalIgnoreCase))
                            {
                                resultRow[column] = row[key];
                                found = true;
                                break;
                            }
                        }
                    }
                    
                    // If column not found, add null for compatibility
                    if (!found)
                    {
                        resultRow[column] = null;
                    }
                }
            }

            // Add aggregate function results (for GROUP BY)
            foreach (var agg in node.AggregateFunctions)
            {
                var key = agg.Alias ?? $"{agg.Type}({agg.ColumnName})";
                if (row.TryGetValue(key, out var aggValue))
                {
                    resultRow[key] = aggValue;
                }
            }

            results.Add(resultRow);
        }

        return results;
    }

    /// <summary>
    /// Execute CREATE TABLE
    /// </summary>
    private object ExecuteCreateTable(CreateTableNode node)
    {
        if (_database.TableExists(node.TableName))
        {
            throw new InvalidOperationException($"Table {node.TableName} already exists");
        }

        // Create table schema (Table.cs compatible format)
        var columnsList = new List<object>(node.Columns.Count);
        foreach (var col in node.Columns)
        {
            // Create anonymous object as in Table.cs
            columnsList.Add(new
            {
                Name = col.Name,
                DataType = ParseDataType(col.DataType).ToString(),
                IsPrimaryKey = col.IsPrimaryKey,
                IsNullable = col.IsNullable,
                IsAutoIncrement = col.IsAutoIncrement,
                IsUnique = col.IsUnique,
                DefaultValue = col.DefaultValue
            });
        }
        
        var schema = new Dictionary<string, object>
        {
            ["Columns"] = columnsList,
            ["Indexes"] = new List<object>()
        };

        // Save empty table with schema
        var storage = _database.GetStorage();
        storage.SaveTable(node.TableName, schema, new List<Dictionary<string, object?>>());

        return new { message = $"Table {node.TableName} created successfully" };
    }

    /// <summary>
    /// Execute DROP TABLE query
    /// </summary>
    private object ExecuteDropTable(DropTableNode node)
    {
        if (!_database.TableExists(node.TableName))
        {
            throw new InvalidOperationException($"Table {node.TableName} not found");
        }

        _database.DropTable(node.TableName);
        return new { message = $"Table {node.TableName} dropped successfully" };
    }

    /// <summary>
    /// Parse data type from string
    /// </summary>
    private DataType ParseDataType(string dataType)
    {
        return dataType.ToUpperInvariant() switch
        {
            "INTEGER" or "INT" => DataType.Integer,
            "LONG" => DataType.Long,
            "DOUBLE" or "DECIMAL" or "FLOAT" => DataType.Double,
            "STRING" or "TEXT" or "VARCHAR" => DataType.String,
            "BOOLEAN" or "BOOL" => DataType.Boolean,
            "DATETIME" or "DATE" or "TIME" => DataType.DateTime,
            "BINARY" or "BLOB" => DataType.Binary,
            _ => DataType.String
        };
    }

    /// <summary>
    /// Execute INSERT query
    /// </summary>
    private object ExecuteInsert(InsertNode node)
    {
        // If table does not exist, create it automatically
        if (!_database.TableExists(node.TableName))
        {
            // Auto-create table from first INSERT
            var columns = new List<CreateTableColumn>();
            if (node.Columns.Count > 0)
            {
                // Use specified columns
                foreach (var colName in node.Columns)
                {
                    columns.Add(new CreateTableColumn
                    {
                        Name = colName,
                        DataType = "STRING", // Default STRING
                        IsNullable = true
                    });
                }
            }
            else if (node.Values.Count > 0 && node.Values[0].Count > 0)
            {
                // Create columns from values
                for (int i = 0; i < node.Values[0].Count; i++)
                {
                    var value = node.Values[0][i];
                    var dataType = InferDataType(value);
                    columns.Add(new CreateTableColumn
                    {
                        Name = $"Column{i + 1}",
                        DataType = dataType,
                        IsNullable = true
                    });
                }
            }

            if (columns.Count > 0)
            {
                var createTableNode = new CreateTableNode
                {
                    TableName = node.TableName,
                    Columns = columns
                };
                ExecuteCreateTable(createTableNode);
            }
        }

        // Load existing data
        var storage = _database.GetStorage();
        var loaded = storage.LoadTable(node.TableName);
        if (!loaded.HasValue)
        {
            throw new InvalidOperationException($"Failed to load table {node.TableName}");
        }

        var (schema, existingRows) = loaded.Value;
        var newRows = new List<Dictionary<string, object>>(existingRows.Count + node.Values.Count);
        
        // Convert existing rows
        foreach (var row in existingRows)
        {
            var newRow = new Dictionary<string, object>();
            foreach (var kvp in row)
            {
                newRow[kvp.Key] = kvp.Value ?? (object)string.Empty;
            }
            newRows.Add(newRow);
        }

                // Get column list from schema
        var schemaColumns = new List<string>();
        if (schema.TryGetValue("Columns", out var columnsObj))
        {
            if (columnsObj is List<object> columnsList)
            {
                foreach (var colObj in columnsList)
                {
                    // Convert column to dictionary (anonymous object or Dictionary)
                    Dictionary<string, object>? colDict = null;
                    if (colObj is Dictionary<string, object> dict)
                    {
                        colDict = dict;
                    }
                    else
                    {
                        // Try convert via JSON
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(colObj);
                            colDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    
                    if (colDict != null && colDict.TryGetValue("Name", out var nameObj))
                    {
                        schemaColumns.Add(nameObj?.ToString() ?? string.Empty);
                    }
                }
            }
        }

        // Validate: all specified columns exist in schema
        if (node.Columns.Count > 0)
        {
            var schemaColumnSet = new HashSet<string>(schemaColumns, StringComparer.OrdinalIgnoreCase);
            foreach (var colName in node.Columns)
            {
                if (!schemaColumnSet.Contains(colName))
                {
                    throw new InvalidOperationException($"Column '{colName}' does not exist in table {node.TableName}");
                }
            }
        }

        // Get AUTOINCREMENT column info
        var autoIncrementColumns = new Dictionary<string, bool>();
        if (schema.TryGetValue("Columns", out var columnsObjForAuto))
        {
            if (columnsObjForAuto is List<object> columnsListForAuto)
            {
                foreach (var colObj in columnsListForAuto)
                {
                    Dictionary<string, object>? colDict = null;
                    if (colObj is Dictionary<string, object> dict)
                    {
                        colDict = dict;
                    }
                    else
                    {
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(colObj);
                            colDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    
                    if (colDict != null && colDict.TryGetValue("Name", out var nameObj) && colDict.TryGetValue("IsAutoIncrement", out var autoIncObj))
                    {
                        var colName = nameObj?.ToString() ?? string.Empty;
                        var isAutoInc = autoIncObj is bool b && b;
                        autoIncrementColumns[colName] = isAutoInc;
                    }
                }
            }
        }

        // Find max value for AUTOINCREMENT columns
        var maxAutoIncrementValues = new Dictionary<string, long>();
        foreach (var existingRow in existingRows)
        {
            foreach (var autoCol in autoIncrementColumns.Where(kvp => kvp.Value))
            {
                var colName = autoCol.Key;
                if (existingRow.TryGetValue(colName, out var value) && value != null)
                {
                    long currentValue = 0;
                    if (value is long l)
                        currentValue = l;
                    else if (value is int i)
                        currentValue = i;
                    else if (long.TryParse(value.ToString(), out var parsed))
                        currentValue = parsed;
                    
                    if (!maxAutoIncrementValues.TryGetValue(colName, out var max) || currentValue > max)
                    {
                        maxAutoIncrementValues[colName] = currentValue;
                    }
                }
            }
        }

        // Insert new rows
        int insertedCount = 0;
        foreach (var valueList in node.Values)
        {
            var row = new Dictionary<string, object>();
            
            if (node.Columns.Count > 0)
            {
                // Use specified columns
                for (int i = 0; i < node.Columns.Count && i < valueList.Count; i++)
                {
                    row[node.Columns[i]] = valueList[i] ?? (object)string.Empty;
                }
            }
            else
            {
                // Use schema column order
                for (int i = 0; i < schemaColumns.Count && i < valueList.Count; i++)
                {
                    row[schemaColumns[i]] = valueList[i] ?? (object)string.Empty;
                }
            }

            // Handle AUTOINCREMENT columns
            foreach (var autoCol in autoIncrementColumns.Where(kvp => kvp.Value))
            {
                var colName = autoCol.Key;
                // If column not in INSERT, generate value
                if (!row.ContainsKey(colName))
                {
                    if (!maxAutoIncrementValues.TryGetValue(colName, out var maxValue))
                    {
                        maxValue = 0;
                    }
                    maxAutoIncrementValues[colName] = maxValue + 1;
                    row[colName] = maxValue + 1;
                }
            }

            newRows.Add(row);
            insertedCount++;
        }

        // Save updated data
        storage.SaveTable(node.TableName, schema, newRows);

        return new { message = $"Inserted {insertedCount} row(s) into table {node.TableName}" };
    }

    /// <summary>
    /// Infer data type from value
    /// </summary>
    private string InferDataType(object? value)
    {
        if (value == null) return "STRING";
        
        return value switch
        {
            int => "INTEGER",
            long => "LONG",
            double or float or decimal => "DOUBLE",
            bool => "BOOLEAN",
            DateTime => "DATETIME",
            byte[] => "BINARY",
            _ => "STRING"
        };
    }

    /// <summary>
    /// Execute UPDATE query
    /// </summary>
    private object ExecuteUpdate(UpdateNode node)
    {
        if (!_database.TableExists(node.TableName))
        {
            throw new InvalidOperationException($"Table {node.TableName} not found");
        }

        // Load table data
        var storage = _database.GetStorage();
        var loaded = storage.LoadTable(node.TableName);
        if (!loaded.HasValue)
        {
            throw new InvalidOperationException($"Failed to load table {node.TableName}");
        }

        var (schema, existingRows) = loaded.Value;
        var updatedRows = new List<Dictionary<string, object>>(existingRows.Count);
        int updatedCount = 0;

        // Get column map for WHERE
        var whereColumnMap = node.Where != null && existingRows.Count > 0
            ? GetColumnMap(existingRows[0], new List<string> { node.Where.Condition.ColumnName ?? string.Empty })
            : new Dictionary<string, string>().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Get column map for SET values
        var setColumnMap = existingRows.Count > 0
            ? GetColumnMap(existingRows[0], node.SetValues.Keys.ToList())
            : new Dictionary<string, string>().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Update rows
        foreach (var row in existingRows)
        {
            var newRow = new Dictionary<string, object>();
            // Copy all existing values
            foreach (var kvp in row)
            {
                newRow[kvp.Key] = kvp.Value ?? (object)string.Empty;
            }
            
            bool shouldUpdate = true;

            // Apply WHERE condition
            if (node.Where != null)
            {
                shouldUpdate = EvaluateConditionOptimized(row, node.Where.Condition, whereColumnMap);
            }

            if (shouldUpdate)
            {
                // Update values
                foreach (var setValue in node.SetValues)
                {
                    if (setColumnMap.TryGetValue(setValue.Key, out var actualKey))
                    {
                        // Use actual key from data
                        newRow[actualKey] = setValue.Value ?? (object)string.Empty;
                    }
                    else
                    {
                        // Fallback: try column by name (case-insensitive)
                        var foundKey = newRow.Keys.FirstOrDefault(k => string.Equals(k, setValue.Key, StringComparison.OrdinalIgnoreCase));
                        if (foundKey != null)
                        {
                            newRow[foundKey] = setValue.Value ?? (object)string.Empty;
                        }
                        else
                        {
                            // If column not found, use name from query
                            newRow[setValue.Key] = setValue.Value ?? (object)string.Empty;
                        }
                    }
                }
                updatedCount++;
            }

            updatedRows.Add(newRow);
        }

        // Save updated data
        storage.SaveTable(node.TableName, schema, updatedRows);

        return new { message = $"Updated {updatedCount} row(s) in table {node.TableName}", count = updatedCount };
    }

    /// <summary>
    /// Execute DELETE query
    /// </summary>
    private object ExecuteDelete(DeleteNode node)
    {
        if (!_database.TableExists(node.TableName))
        {
            throw new InvalidOperationException($"Table {node.TableName} not found");
        }

        // Load table data
        var storage = _database.GetStorage();
        var loaded = storage.LoadTable(node.TableName);
        if (!loaded.HasValue)
        {
            throw new InvalidOperationException($"Failed to load table {node.TableName}");
        }

        var (schema, existingRows) = loaded.Value;
        var remainingRows = new List<Dictionary<string, object>>(existingRows.Count);
        int deletedCount = 0;

        // Get column map for WHERE
        var columnMap = node.Where != null && existingRows.Count > 0
            ? GetColumnMap(existingRows[0], new List<string> { node.Where.Condition.ColumnName ?? string.Empty })
            : new Dictionary<string, string>().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Delete rows
        foreach (var row in existingRows)
        {
            bool shouldDelete = true;

            // Apply WHERE condition
            if (node.Where != null)
            {
                shouldDelete = EvaluateConditionOptimized(row, node.Where.Condition, columnMap);
            }

            if (!shouldDelete)
            {
                remainingRows.Add(row);
            }
            else
            {
                deletedCount++;
            }
        }

        // Save remaining data
        storage.SaveTable(node.TableName, schema, remainingRows);

        return new { message = $"Deleted {deletedCount} row(s) from table {node.TableName}", count = deletedCount };
    }

    /// <summary>
    /// Load table data (optimized)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Dictionary<string, object?>> LoadTableData(string tableName)
    {
        var loaded = _database.GetStorage().LoadTable(tableName);
        if (!loaded.HasValue)
        {
            return new List<Dictionary<string, object?>>();
        }

        var (_, rows) = loaded.Value;
        var result = new List<Dictionary<string, object?>>(rows.Count);
        
        // Direct conversion without extra allocations
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>(row.Count);
            foreach (var kvp in row)
            {
                dict[kvp.Key] = kvp.Value;
            }
            result.Add(dict);
        }
        
        return result;
    }
    
    /// <summary>
    /// Get column map (cached)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FrozenDictionary<string, string> GetColumnMap(Dictionary<string, object?> sampleRow, List<string> queryColumns)
    {
        var cacheKey = string.Join("|", queryColumns);
        
        lock (_cacheLock)
        {
            if (_columnCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var map = new Dictionary<string, string>(queryColumns.Count, StringComparer.OrdinalIgnoreCase);
            var rowKeys = sampleRow.Keys;
            
            foreach (var queryCol in queryColumns)
            {
                foreach (var rowKey in rowKeys)
                {
                    if (string.Equals(queryCol, rowKey, StringComparison.OrdinalIgnoreCase))
                    {
                        map[queryCol] = rowKey;
                        break;
                    }
                }
            }

            var frozen = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            _columnCache[cacheKey] = frozen;
            return frozen;
        }
    }

    /// <summary>
    /// Apply WHERE conditions (early exit)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Dictionary<string, object?>> ApplyWhereConditionsOptimized(
        List<Dictionary<string, object?>> data, 
        ConditionNode condition,
        FrozenDictionary<string, string> columnMap)
    {
        var result = new List<Dictionary<string, object?>>(data.Count);
        
        foreach (var row in data)
        {
            if (EvaluateConditionOptimized(row, condition, columnMap))
            {
                result.Add(row);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Evaluate condition
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EvaluateConditionOptimized(
        Dictionary<string, object?> row, 
        ConditionNode condition,
        FrozenDictionary<string, string> columnMap)
    {
        if (condition.Left != null && condition.Right != null)
        {
            var leftResult = EvaluateConditionOptimized(row, condition.Left, columnMap);
            
            // Early exit for AND
            if (condition.LogicalOp == LogicalOperator.And && !leftResult)
            {
                return false;
            }
            
            // Early exit for OR
            if (condition.LogicalOp == LogicalOperator.Or && leftResult)
            {
                return true;
            }
            
            var rightResult = EvaluateConditionOptimized(row, condition.Right, columnMap);

            return condition.LogicalOp switch
            {
                LogicalOperator.And => leftResult && rightResult,
                LogicalOperator.Or => leftResult || rightResult,
                _ => false
            };
        }

        if (condition.ColumnName == null)
        {
            return true;
        }

        // Use cached column map
        if (!columnMap.TryGetValue(condition.ColumnName, out var columnKey))
        {
            // Try direct lookup (fallback)
            foreach (var key in row.Keys)
            {
                if (string.Equals(key, condition.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    columnKey = key;
                    break;
                }
            }
            
            if (columnKey == null)
            {
                return false;
            }
        }

        if (!row.TryGetValue(columnKey, out var value))
        {
            return false;
        }

        var result = condition.Operator switch
        {
            ComparisonOperator.Equals => FastEquals(value, condition.Value),
            ComparisonOperator.NotEquals => !FastEquals(value, condition.Value),
            ComparisonOperator.GreaterThan => FastCompare(value, condition.Value) > 0,
            ComparisonOperator.GreaterThanOrEqual => FastCompare(value, condition.Value) >= 0,
            ComparisonOperator.LessThan => FastCompare(value, condition.Value) < 0,
            ComparisonOperator.LessThanOrEqual => FastCompare(value, condition.Value) <= 0,
            ComparisonOperator.Like => EvaluateLikeOptimized(value, condition.Value),
            ComparisonOperator.In => EvaluateInOptimized(value, condition.Values),
            ComparisonOperator.NotIn => !EvaluateInOptimized(value, condition.Values),
            _ => false
        };

        return condition.IsNegated ? !result : result;
    }
    
    /// <summary>
    /// Fast value comparison
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool FastEquals(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        
        // Optimize for primitive types
        if (a is int intA && b is int intB) return intA == intB;
        if (a is long longA && b is long longB) return longA == longB;
        if (a is double doubleA && b is double doubleB) return doubleA == doubleB;
        if (a is string strA && b is string strB)
        {
            return strA.Length == strB.Length && 
                   string.Equals(strA, strB, StringComparison.OrdinalIgnoreCase);
        }
        
        return Equals(a, b);
    }

    /// <summary>
    /// Fast value comparison
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FastCompare(object? a, object? b)
    {
        if (a == null || b == null) return 0;

        // Optimize for primitive types
        if (a is int intA && b is int intB) return intA.CompareTo(intB);
        if (a is long longA && b is long longB) return longA.CompareTo(longB);
        if (a is double doubleA && b is double doubleB) return doubleA.CompareTo(doubleB);
        if (a is IComparable comparable && a.GetType() == b.GetType())
        {
            return comparable.CompareTo(b);
        }

        try
        {
            if (IsNumericFast(a) && IsNumericFast(b))
            {
                var aDecimal = Convert.ToDecimal(a);
                var bDecimal = Convert.ToDecimal(b);
                return aDecimal.CompareTo(bDecimal);
            }

            return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Fast number check
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsNumericFast(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }


    /// <summary>
    /// Evaluate LIKE condition
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EvaluateLikeOptimized(object? value, object? pattern)
    {
        if (value == null || pattern == null)
            return false;

        var valueStr = value.ToString();
        var patternStr = pattern.ToString();
        
        if (string.IsNullOrEmpty(valueStr) || string.IsNullOrEmpty(patternStr))
            return false;

        // Fast path for simple cases
        if (!patternStr.Contains('%') && !patternStr.Contains('_'))
        {
            return valueStr.Contains(patternStr, StringComparison.OrdinalIgnoreCase);
        }
        
        // Use Span for optimization
        var patternSpan = patternStr.AsSpan();
        var valueSpan = valueStr.AsSpan();
        
        return MatchLikePattern(valueSpan, patternSpan);
    }

    /// <summary>
    /// LIKE pattern matching (iterative)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MatchLikePattern(ReadOnlySpan<char> value, ReadOnlySpan<char> pattern)
    {
        // Iterative algorithm instead of recursive
        int valueIndex = 0;
        int patternIndex = 0;
        int lastStarIndex = -1;
        int lastValueIndex = -1;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '%')
            {
                // Remember position for backtracking
                lastStarIndex = patternIndex;
                lastValueIndex = valueIndex;
                patternIndex++;
                
                // If % at end of pattern - all matches
                if (patternIndex >= pattern.Length)
                    return true;
            }
            else if (patternIndex < pattern.Length && 
                     (pattern[patternIndex] == '_' || 
                      char.ToLowerInvariant(value[valueIndex]) == char.ToLowerInvariant(pattern[patternIndex])))
            {
                // Character match or _
                patternIndex++;
                valueIndex++;
            }
            else if (lastStarIndex >= 0)
            {
                // Backtrack to last %
                patternIndex = lastStarIndex + 1;
                lastValueIndex++;
                valueIndex = lastValueIndex;
            }
            else
            {
                return false;
            }
        }

        // Skip remaining % and _
        while (patternIndex < pattern.Length && (pattern[patternIndex] == '%' || pattern[patternIndex] == '_'))
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>
    /// Evaluate IN condition
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EvaluateInOptimized(object? value, List<object?>? values)
    {
        if (values == null || values.Count == 0)
            return false;

        if (value == null)
        {
            return values.Contains(null);
        }

        // Optimize for primitive types
        if (value is int intValue)
        {
            foreach (var val in values)
            {
                if (val is int intVal && intValue == intVal)
                    return true;
            }
        }
        else if (value is string strValue)
        {
            foreach (var val in values)
            {
                if (val is string strVal && string.Equals(strValue, strVal, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        else
        {
            foreach (var val in values)
            {
                if (FastEquals(value, val))
                    return true;
                
                if (IsNumericFast(value) && IsNumericFast(val))
                {
                    try
                    {
                        if (Convert.ToDecimal(value) == Convert.ToDecimal(val))
                            return true;
                    }
                    catch { }
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Apply GROUP BY
    /// </summary>
    private List<Dictionary<string, object?>> ApplyGroupByOptimized(
        List<Dictionary<string, object?>> data,
        GroupByNode groupBy,
        List<AggregateFunction> aggregates,
        FrozenDictionary<string, string> columnMap)
    {
        var groups = new Dictionary<string, List<Dictionary<string, object?>>>(data.Count);
        
        foreach (var row in data)
        {
            var keyBuilder = new StringBuilder(64);
            bool first = true;
            
            foreach (var col in groupBy.Columns)
            {
                if (!first) keyBuilder.Append('|');
                first = false;
                
                if (columnMap.TryGetValue(col, out var actualKey) && row.TryGetValue(actualKey, out var val))
                {
                    keyBuilder.Append(actualKey).Append('=').Append(val);
                }
            }
            
            var key = keyBuilder.ToString();
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<Dictionary<string, object?>>();
                groups[key] = group;
            }
            group.Add(row);
        }

        var results = new List<Dictionary<string, object?>>(groups.Count);
        
        foreach (var (key, group) in groups)
        {
            var result = new Dictionary<string, object?>();
            var firstRow = group[0];
            
            foreach (var col in groupBy.Columns)
            {
                if (columnMap.TryGetValue(col, out var actualKey) && firstRow.TryGetValue(actualKey, out var val))
                {
                    result[actualKey] = val;
                }
            }

            foreach (var agg in aggregates)
            {
                var aggKey = agg.Alias ?? $"{agg.Type}({agg.ColumnName})";
                result[aggKey] = CalculateAggregateOptimized(group, agg, columnMap);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Apply ORDER BY (Array.Sort)
    /// </summary>
    private List<Dictionary<string, object?>> ApplyOrderByOptimized(
        List<Dictionary<string, object?>> data,
        OrderByNode orderBy,
        FrozenDictionary<string, string> columnMap)
    {
        if (data.Count == 0) return data;
        
        // Find column keys once
        var columnKeys = new string[orderBy.Items.Count];
        for (int i = 0; i < orderBy.Items.Count; i++)
        {
            var item = orderBy.Items[i];
            if (columnMap.TryGetValue(item.ColumnName, out var key))
            {
                columnKeys[i] = key;
            }
            else
            {
                // Fallback search
                foreach (var k in data[0].Keys)
                {
                    if (string.Equals(k, item.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        columnKeys[i] = k;
                        break;
                    }
                }
            }
        }

        // Use Array.Sort for large data (faster than LINQ)
        if (data.Count > 1000)
        {
            var array = data.ToArray();
            Array.Sort(array, (a, b) =>
            {
                for (int i = 0; i < orderBy.Items.Count; i++)
                {
                    var item = orderBy.Items[i];
                    var colKey = columnKeys[i];
                    
                    object? valA = colKey != null && a.TryGetValue(colKey, out var vA) ? vA : null;
                    object? valB = colKey != null && b.TryGetValue(colKey, out var vB) ? vB : null;
                    
                    var comparison = FastCompare(valA, valB);
                    if (comparison != 0)
                    {
                        return item.Descending ? -comparison : comparison;
                    }
                }
                return 0;
            });
            return array.ToList();
        }

        // For small data use LINQ (more readable)
        IOrderedEnumerable<Dictionary<string, object?>>? ordered = null;

        for (int i = 0; i < orderBy.Items.Count; i++)
        {
            var item = orderBy.Items[i];
            var colKey = columnKeys[i];
            
            if (ordered == null)
            {
                ordered = item.Descending
                    ? data.OrderByDescending(row => colKey != null && row.TryGetValue(colKey, out var v) ? v : null)
                    : data.OrderBy(row => colKey != null && row.TryGetValue(colKey, out var v) ? v : null);
            }
            else
            {
                ordered = item.Descending
                    ? ordered.ThenByDescending(row => colKey != null && row.TryGetValue(colKey, out var v) ? v : null)
                    : ordered.ThenBy(row => colKey != null && row.TryGetValue(colKey, out var v) ? v : null);
            }
        }

        return ordered?.ToList() ?? data;
    }

    /// <summary>
    /// Compute aggregate function
    /// </summary>
    private object? CalculateAggregateOptimized(
        List<Dictionary<string, object?>> data, 
        AggregateFunction agg,
        FrozenDictionary<string, string> columnMap)
    {
        if (agg.Type == AggregateType.Count && agg.ColumnName == "*")
        {
            return data.Count;
        }

        if (agg.ColumnName == "*")
        {
            return data.Count;
        }

        string? columnKey = null;
        if (!columnMap.TryGetValue(agg.ColumnName, out columnKey))
        {
            // Fallback search
            if (data.Count > 0)
            {
                foreach (var key in data[0].Keys)
                {
                    if (string.Equals(key, agg.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        columnKey = key;
                        break;
                    }
                }
            }
        }

        if (columnKey == null)
        {
            return agg.Type == AggregateType.Count ? 0 : null;
        }

        if (agg.Type == AggregateType.Count)
        {
            int count = 0;
            foreach (var row in data)
            {
                if (row.ContainsKey(columnKey))
                    count++;
            }
            return count;
        }

        // For numeric aggregates
        decimal sum = 0;
        int numericCount = 0;
        decimal? min = null;
        decimal? max = null;

        foreach (var row in data)
        {
            if (row.TryGetValue(columnKey, out var val) && val != null && IsNumericFast(val))
            {
                var dec = Convert.ToDecimal(val);
                sum += dec;
                numericCount++;
                
                if (min == null || dec < min.Value)
                    min = dec;
                if (max == null || dec > max.Value)
                    max = dec;
            }
        }

        return agg.Type switch
        {
            AggregateType.Sum => sum,
            AggregateType.Avg => numericCount > 0 ? (object)(double)(sum / numericCount) : null,
            AggregateType.Min => min,
            AggregateType.Max => max,
            _ => null
        };
    }
    
    /// <summary>
    /// Compute aggregate (backward compatibility)
    /// </summary>
    private object? CalculateAggregate(List<Dictionary<string, object?>> data, AggregateFunction agg)
    {
        // Create temporary column map
        var columnMap = data.Count > 0 
            ? GetColumnMap(data[0], new List<string> { agg.ColumnName })
            : new Dictionary<string, string>().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        
        return CalculateAggregateOptimized(data, agg, columnMap);
    }

    /// <summary>
    /// Execute MODEL ADD
    /// </summary>
    private object ExecuteModelAdd(ModelAddNode node)
    {
        if (_modelService == null)
        {
            throw new InvalidOperationException("ModelService not initialized");
        }

        var result = _modelService.AddModel(node.ModelName, node.Fields, node.ModelType);
        if (!result)
        {
            throw new InvalidOperationException($"Model {node.ModelName} already exists");
        }

        return new { message = $"Model {node.ModelName} created successfully", type = node.ModelType };
    }

    /// <summary>
    /// Execute MODEL LIST
    /// </summary>
    private object ExecuteModelList(ModelListNode node)
    {
        if (_modelService == null)
        {
            throw new InvalidOperationException("ModelService not initialized");
        }

        var models = _modelService.ListModels();
        return new
        {
            models = models.Select(m => new
            {
                name = m.Name,
                type = m.Type,
                fieldCount = m.FieldCount,
                createdAt = m.CreatedAt
            }).ToList(),
            count = models.Count
        };
    }

    /// <summary>
    /// Execute MODEL EDIT
    /// </summary>
    private object ExecuteModelEdit(ModelEditNode node)
    {
        if (_modelService == null)
        {
            throw new InvalidOperationException("ModelService not initialized");
        }

        var result = _modelService.EditModel(node.ModelName, node.OldFields, node.NewFields);
        if (!result)
        {
            throw new InvalidOperationException($"Model {node.ModelName} not found");
        }

        return new { message = $"Model {node.ModelName} updated successfully" };
    }

    /// <summary>
    /// Execute MODEL SEE
    /// </summary>
    private object ExecuteModelSee(ModelSeeNode node)
    {
        if (_modelService == null)
        {
            throw new InvalidOperationException("ModelService not initialized");
        }

        var details = _modelService.SeeModel(node.ModelName);
        if (details == null)
        {
            throw new InvalidOperationException($"Model {node.ModelName} not found");
        }

        return new
        {
            name = details.Name,
            type = details.Type,
            fields = details.Fields.Select(f => new
            {
                name = f.Name,
                type = f.Type,
                isKey = f.IsKey,
                isNullable = f.IsNullable,
                defaultValue = f.DefaultValue
            }).ToList(),
            createdAt = details.CreatedAt,
            updatedAt = details.UpdatedAt
        };
    }

    /// <summary>
    /// Execute MODEL DEL
    /// </summary>
    private object ExecuteModelDel(ModelDelNode node)
    {
        if (_modelService == null)
        {
            throw new InvalidOperationException("ModelService not initialized");
        }

        var result = _modelService.DeleteModel(node.ModelName, node.FieldNames);
        if (!result)
        {
            throw new InvalidOperationException($"Model {node.ModelName} not found");
        }

        if (node.FieldNames != null && node.FieldNames.Count > 0)
        {
            return new { message = $"Fields {string.Join(", ", node.FieldNames)} removed from model {node.ModelName}" };
        }
        else
        {
            return new { message = $"Model {node.ModelName} deleted successfully" };
        }
    }

    /// <summary>
    /// Execute MODEL USE
    /// </summary>
    private object ExecuteModelUse(ModelUseNode node)
    {
        if (_modelService == null)
        {
            throw new InvalidOperationException("ModelService not initialized");
        }

        var usage = _modelService.UseModel(node.ModelName);
        if (usage == null)
        {
            throw new InvalidOperationException($"Model {node.ModelName} not found");
        }

        return new
        {
            modelName = usage.ModelName,
            databases = usage.Databases,
            tables = usage.Tables,
            usageCount = usage.Databases.Count + usage.Tables.Count
        };
    }
}
