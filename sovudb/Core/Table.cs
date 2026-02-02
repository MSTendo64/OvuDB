using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using ovudb.Query;
using ovudb.Storage;

namespace ovudb.Core;

/// <summary>
/// Typed database table
/// </summary>
public class Table<T> where T : class, new()
{
    private readonly string _tableName;
    private readonly List<Column> _columns;
    private readonly List<Index> _indexes;
    private readonly IStorage _storage;
    private readonly List<T> _data;
    private readonly Dictionary<string, PropertyInfo> _propertyMap;
    private bool _isLoaded = false;
    private bool _autoSave = true; // Auto-save on each insert

    public Table(string tableName, IStorage storage)
    {
        _tableName = tableName;
        _storage = storage;
        _columns = new List<Column>();
        _indexes = new List<Index>();
        _data = new List<T>();
        _propertyMap = typeof(T).GetProperties()
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Add column
    /// </summary>
    public Table<T> AddColumn(Column column)
    {
        _columns.Add(column);
        return this;
    }

    /// <summary>
    /// Add column by class property
    /// </summary>
    public Table<T> AddColumn(string propertyName, DataType dataType)
    {
        var column = new Column(propertyName, dataType);
        _columns.Add(column);
        return this;
    }

    /// <summary>
    /// Add index
    /// </summary>
    public Table<T> AddIndex(Index index)
    {
        _indexes.Add(index);
        return this;
    }

    /// <summary>
    /// Create table (if not exists)
    /// </summary>
    public void CreateIfNotExists()
    {
        if (_storage.TableExists(_tableName))
        {
            Load();
            return;
        }

        // Auto-create columns from class properties if not set
        if (_columns.Count == 0)
        {
            AutoCreateColumns();
        }

        Save();
    }

    /// <summary>
    /// Auto-create columns from class properties
    /// </summary>
    private void AutoCreateColumns()
    {
        foreach (var prop in typeof(T).GetProperties())
        {
            var dataType = DataTypeExtensions.FromClrType(prop.PropertyType);
            var column = new Column(prop.Name, dataType);
            
            // Check attributes for additional configuration
            if (prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                prop.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                column.PrimaryKey().AutoIncrement();
            }

            _columns.Add(column);
        }
    }

    // Cache for max ID (for autoincrement optimization)
    private int? _cachedMaxIntId;
    private long? _cachedMaxLongId;
    private bool _maxIdCacheValid = false;

    /// <summary>
    /// Insert record
    /// </summary>
    public T Insert(T entity)
    {
        EnsureLoaded();

        // Autoincrement handling (optimized)
        var primaryKeyColumn = _columns.FirstOrDefault(c => c.IsPrimaryKey && c.IsAutoIncrement);
        if (primaryKeyColumn != null)
        {
            var prop = _propertyMap.GetValueOrDefault(primaryKeyColumn.Name);
            if (prop != null)
            {
                var currentValue = prop.GetValue(entity);
                if (currentValue == null || (currentValue is int intVal && intVal == 0) ||
                    (currentValue is long longVal && longVal == 0))
                {
                    var propertyType = prop.PropertyType;
                    object newId;

                    if (propertyType == typeof(int))
                    {
                        if (!_maxIdCacheValid || _cachedMaxIntId == null)
                        {
                            // Compute max ID only when cache is invalid
                            _cachedMaxIntId = _data
                                .Select(e => prop.GetValue(e))
                                .Where(v => v != null)
                                .Select(v => Convert.ToInt32(v))
                                .DefaultIfEmpty(0)
                                .Max();
                            _maxIdCacheValid = true;
                        }
                        _cachedMaxIntId = _cachedMaxIntId.Value + 1;
                        newId = _cachedMaxIntId.Value;
                    }
                    else if (propertyType == typeof(long))
                    {
                        if (!_maxIdCacheValid || _cachedMaxLongId == null)
                        {
                            _cachedMaxLongId = _data
                                .Select(e => prop.GetValue(e))
                                .Where(v => v != null)
                                .Select(v => Convert.ToInt64(v))
                                .DefaultIfEmpty(0L)
                                .Max();
                            _maxIdCacheValid = true;
                        }
                        _cachedMaxLongId = _cachedMaxLongId.Value + 1L;
                        newId = _cachedMaxLongId.Value;
                    }
                    else
                    {
                        // For other numeric types use common approach
                        var maxId = _data
                            .Select(e => prop.GetValue(e))
                            .Where(v => v != null)
                            .Select(v => Convert.ToDecimal(v))
                            .DefaultIfEmpty(0m)
                            .Max();
                        newId = Convert.ChangeType(maxId + 1, propertyType);
                    }

                    prop.SetValue(entity, newId);
                }
            }
        }

        _data.Add(entity);
        // Save only if auto-save is enabled
        if (_autoSave)
        {
            Save();
        }
        return entity;
    }

    /// <summary>
    /// Enable/disable auto-save
    /// </summary>
    public Table<T> SetAutoSave(bool enabled)
    {
        _autoSave = enabled;
        return this;
    }

    /// <summary>
    /// Force save data
    /// </summary>
    public void Flush()
    {
        Save();
    }

    /// <summary>
    /// Force reload data from disk
    /// </summary>
    public void Reload()
    {
        _isLoaded = false;
        Load();
    }

    /// <summary>
    /// Batch insert records (optimized - saves once)
    /// </summary>
    public void InsertBatch(IEnumerable<T> entities)
    {
        EnsureLoaded();
        var entityList = entities.ToList();
        if (entityList.Count == 0) return;

        // Autoincrement handling for all records
        var primaryKeyColumn = _columns.FirstOrDefault(c => c.IsPrimaryKey && c.IsAutoIncrement);
        if (primaryKeyColumn != null)
        {
            var prop = _propertyMap.GetValueOrDefault(primaryKeyColumn.Name);
            if (prop != null)
            {
                var propertyType = prop.PropertyType;
                
                // Compute max ID once
                if (propertyType == typeof(int))
                {
                    if (!_maxIdCacheValid || _cachedMaxIntId == null)
                    {
                        _cachedMaxIntId = _data
                            .Select(e => prop.GetValue(e))
                            .Where(v => v != null)
                            .Select(v => Convert.ToInt32(v))
                            .DefaultIfEmpty(0)
                            .Max();
                        _maxIdCacheValid = true;
                    }
                    
                    foreach (var entity in entityList)
                    {
                        var currentValue = prop.GetValue(entity);
                        if (currentValue == null || (currentValue is int intVal && intVal == 0))
                        {
                            _cachedMaxIntId = _cachedMaxIntId.Value + 1;
                            prop.SetValue(entity, _cachedMaxIntId.Value);
                        }
                        else if (currentValue is int existingId && existingId > _cachedMaxIntId)
                        {
                            _cachedMaxIntId = existingId;
                        }
                    }
                }
                else if (propertyType == typeof(long))
                {
                    if (!_maxIdCacheValid || _cachedMaxLongId == null)
                    {
                        _cachedMaxLongId = _data
                            .Select(e => prop.GetValue(e))
                            .Where(v => v != null)
                            .Select(v => Convert.ToInt64(v))
                            .DefaultIfEmpty(0L)
                            .Max();
                        _maxIdCacheValid = true;
                    }
                    
                    foreach (var entity in entityList)
                    {
                        var currentValue = prop.GetValue(entity);
                        if (currentValue == null || (currentValue is long longVal && longVal == 0))
                        {
                            _cachedMaxLongId = _cachedMaxLongId.Value + 1L;
                            prop.SetValue(entity, _cachedMaxLongId.Value);
                        }
                        else if (currentValue is long existingId && existingId > _cachedMaxLongId)
                        {
                            _cachedMaxLongId = existingId;
                        }
                    }
                }
                else
                {
                    // For other types use standard approach
                    var maxId = _data
                        .Select(e => prop.GetValue(e))
                        .Where(v => v != null)
                        .Select(v => Convert.ToDecimal(v))
                        .DefaultIfEmpty(0m)
                        .Max();
                    
                    foreach (var entity in entityList)
                    {
                        var currentValue = prop.GetValue(entity);
                        if (currentValue == null || 
                            (currentValue is int intVal && intVal == 0) ||
                            (currentValue is long longVal && longVal == 0))
                        {
                            maxId += 1;
                            prop.SetValue(entity, Convert.ChangeType(maxId, propertyType));
                        }
                    }
                }
            }
        }

        // Add all records to memory
        _data.AddRange(entityList);
        
        // Save once for entire batch (always save on batch insert)
        Save();
        
        // Restore auto-save setting
        if (!_autoSave)
        {
            _autoSave = true;
        }
    }

    /// <summary>
    /// Update record
    /// </summary>
    public bool Update(T entity)
    {
        EnsureLoaded();

        T? existing = null;

        // Try to find by primary key
        var primaryKeyColumn = _columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (primaryKeyColumn != null)
        {
            var prop = _propertyMap.GetValueOrDefault(primaryKeyColumn.Name);
            if (prop != null)
            {
                var keyValue = prop.GetValue(entity);
                existing = _data.FirstOrDefault(e => Equals(prop.GetValue(e), keyValue));
            }
        }

        // If not found by primary key, try to find by object reference
        if (existing == null)
        {
            var index = _data.IndexOf(entity);
            if (index >= 0)
            {
                existing = _data[index];
            }
        }

        // If still not found, try to find by equality of all fields
        if (existing == null)
        {
            existing = _data.FirstOrDefault(e => AreEntitiesEqual(e, entity));
        }

        if (existing == null)
        {
            return false;
        }

        // Copy property values
        foreach (var p in typeof(T).GetProperties())
        {
            if (p.CanWrite)
            {
                var value = p.GetValue(entity);
                p.SetValue(existing, value);
            }
        }

        Save();
        return true;
    }

    /// <summary>
    /// Check equality of two entities by all properties
    /// </summary>
    private bool AreEntitiesEqual(T entity1, T entity2)
    {
        foreach (var prop in typeof(T).GetProperties())
        {
            var value1 = prop.GetValue(entity1);
            var value2 = prop.GetValue(entity2);

            if (!Equals(value1, value2))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Delete record
    /// </summary>
    public bool Delete(T entity)
    {
        EnsureLoaded();

        var result = _data.Remove(entity);
        if (result)
        {
            Save();
        }
        return result;
    }

    /// <summary>
    /// Delete records by condition
    /// </summary>
    public int Delete(Expression<Func<T, bool>> predicate)
    {
        EnsureLoaded();

        var query = new QueryBuilder<T>(this).Where(predicate);
        var toDelete = ExecuteQuery(query).ToList();
        var count = toDelete.Count;

        foreach (var item in toDelete)
        {
            _data.Remove(item);
        }

        if (count > 0)
        {
            Save();
        }

        return count;
    }

    /// <summary>
    /// Get query builder
    /// </summary>
    public QueryBuilder<T> Query()
    {
        EnsureLoaded();
        return new QueryBuilder<T>(this);
    }

    /// <summary>
    /// Execute query
    /// </summary>
    internal List<T> ExecuteQuery(QueryBuilder<T> queryBuilder)
    {
        EnsureLoaded();

        // Check query cache if storage supports it
        BinaryStorage? binaryStorage = _storage as BinaryStorage;
        if (binaryStorage != null)
        {
            var queryKey = QueryCache.GenerateKey(_tableName, queryBuilder.ToString());
            var cached = binaryStorage.QueryCache.Get<List<T>>(queryKey);
            if (cached != null)
            {
                return cached;
            }
        }

        var results = _data.AsEnumerable();

        // Apply WHERE conditions
        if (queryBuilder.WhereConditions.Any())
        {
            foreach (var condition in queryBuilder.WhereConditions)
            {
                var prop = _propertyMap.GetValueOrDefault(condition.ColumnName);
                if (prop == null) continue;

                if (condition.IsOr)
                {
                    // For OR add results to current
                    results = results.Union(ApplyCondition(_data, condition));
                }
                else
                {
                    // For AND apply condition to already filtered results
                    results = results.Where(item =>
                    {
                        var value = prop.GetValue(item);
                        return MatchesCondition(value, condition.Value, condition.Operator);
                    });
                }
            }
        }

        // Apply sorting
        if (queryBuilder.OrderByColumns.Any())
        {
            var firstColumn = queryBuilder.OrderByColumns.First();
            var orderProp = _propertyMap.GetValueOrDefault(firstColumn);
            if (orderProp != null)
            {
                if (queryBuilder.IsOrderByDescending)
                {
                    results = results.OrderByDescending(e => orderProp.GetValue(e));
                }
                else
                {
                    results = results.OrderBy(e => orderProp.GetValue(e));
                }
            }
        }

        // Apply OFFSET and LIMIT
        if (queryBuilder.OffsetCount.HasValue)
        {
            results = results.Skip(queryBuilder.OffsetCount.Value);
        }

        if (queryBuilder.LimitCount.HasValue)
        {
            results = results.Take(queryBuilder.LimitCount.Value);
        }

        var resultList = results.ToList();
        
        // Save result to query cache
        if (binaryStorage != null)
        {
            var queryKey = QueryCache.GenerateKey(_tableName, queryBuilder.ToString());
            binaryStorage.QueryCache.Put(queryKey, resultList, _tableName);
        }

        return resultList;
    }

    /// <summary>
    /// Count records by query
    /// </summary>
    internal int ExecuteCount(QueryBuilder<T> queryBuilder)
    {
        return ExecuteQuery(queryBuilder).Count;
    }

    /// <summary>
    /// Apply WHERE condition
    /// </summary>
    private IEnumerable<T> ApplyCondition(IEnumerable<T> source, WhereCondition condition)
    {
        var prop = _propertyMap.GetValueOrDefault(condition.ColumnName);
        if (prop == null) yield break;

        foreach (var item in source)
        {
            var value = prop.GetValue(item);
            if (MatchesCondition(value, condition.Value, condition.Operator))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Check condition match
    /// </summary>
    private bool MatchesCondition(object? value, object? conditionValue, ComparisonOperator op)
    {
        if (value == null && conditionValue == null)
            return op == ComparisonOperator.Equals;

        if (value == null || conditionValue == null)
            return op == ComparisonOperator.NotEquals;

        return op switch
        {
            ComparisonOperator.Equals => Equals(value, conditionValue),
            ComparisonOperator.NotEquals => !Equals(value, conditionValue),
            ComparisonOperator.GreaterThan => CompareValues(value, conditionValue) > 0,
            ComparisonOperator.GreaterThanOrEqual => CompareValues(value, conditionValue) >= 0,
            ComparisonOperator.LessThan => CompareValues(value, conditionValue) < 0,
            ComparisonOperator.LessThanOrEqual => CompareValues(value, conditionValue) <= 0,
            ComparisonOperator.Like => value.ToString()?.Contains(conditionValue.ToString() ?? "") ?? false,
            _ => false
        };
    }

    /// <summary>
    /// Compare values
    /// </summary>
    private int CompareValues(object? a, object? b)
    {
        if (a == null || b == null) return 0;

        // If types match, use direct comparison
        if (a.GetType() == b.GetType() && a is IComparable comparable)
        {
            return comparable.CompareTo(b);
        }

        // Convert to common type for comparison
        // Priority: decimal > double > long > int
        try
        {
            if (IsNumericType(a) && IsNumericType(b))
            {
                var aDecimal = Convert.ToDecimal(a);
                var bDecimal = Convert.ToDecimal(b);
                return aDecimal.CompareTo(bDecimal);
            }

            // For strings use string comparison
            if (a is string || b is string)
            {
                return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
            }

            // For dates
            if (a is DateTime dateA && b is DateTime dateB)
            {
                return dateA.CompareTo(dateB);
            }

            // Try to convert to common type
            if (a is IComparable comparableA)
            {
                // Try to convert b to type of a
                var convertedB = Convert.ChangeType(b, a.GetType());
                return comparableA.CompareTo(convertedB);
            }
        }
        catch
        {
            // If conversion failed, use string comparison
        }

        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Check if type is numeric
    /// </summary>
    private bool IsNumericType(object? value)
    {
        if (value == null) return false;
        var type = value.GetType();
        return type == typeof(int) || type == typeof(long) || type == typeof(float) ||
               type == typeof(double) || type == typeof(decimal) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
               type == typeof(ushort) || type == typeof(sbyte);
    }

    /// <summary>
    /// Load data from storage
    /// </summary>
    private void Load()
    {
        if (_isLoaded) return;

        var loaded = _storage.LoadTable(_tableName);
        if (loaded.HasValue)
        {
            var (schema, rows) = loaded.Value;
            _data.Clear();

            foreach (var row in rows)
            {
                var entity = new T();
                foreach (var prop in typeof(T).GetProperties())
                {
                    // Try exact match first
                    if (row.TryGetValue(prop.Name, out var value) && prop.CanWrite)
                    {
                        var convertedValue = ConvertValue(value, prop.PropertyType);
                        prop.SetValue(entity, convertedValue);
                    }
                    else
                    {
                        // Try case-insensitive match
                        var matchedKey = row.Keys.FirstOrDefault(k => 
                            string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));
                        if (matchedKey != null && prop.CanWrite)
                        {
                            var matchedValue = row[matchedKey];
                            var convertedValue = ConvertValue(matchedValue, prop.PropertyType);
                            prop.SetValue(entity, convertedValue);
                        }
                    }
                }
                _data.Add(entity);
            }
        }

        // Invalidate max ID cache on load
        _maxIdCacheValid = false;
        _cachedMaxIntId = null;
        _cachedMaxLongId = null;

        _isLoaded = true;
    }

    /// <summary>
    /// Save data to storage (optimized)
    /// </summary>
    private void Save()
    {
        // Optimization: create schema once
        var schema = new Dictionary<string, object>(2);
        var columnsList = new List<object>(_columns.Count);
        foreach (var c in _columns)
        {
            columnsList.Add(new
            {
                c.Name,
                c.DataType,
                c.IsPrimaryKey,
                c.IsNullable,
                c.IsAutoIncrement
            });
        }
        schema["Columns"] = columnsList;

        var indexesList = new List<object>(_indexes.Count);
        foreach (var i in _indexes)
        {
            indexesList.Add(new
            {
                i.Name,
                i.ColumnNames,
                i.IsUnique
            });
        }
        schema["Indexes"] = indexesList;

        // Optimization: convert data without LINQ, with pre-allocation
        var rows = new List<Dictionary<string, object>>(_data.Count);
        var properties = typeof(T).GetProperties();
        
        foreach (var entity in _data)
        {
            var row = new Dictionary<string, object>(properties.Length);
            foreach (var prop in properties)
            {
                var value = prop.GetValue(entity);
                row[prop.Name] = value ?? (object)string.Empty;
            }
            rows.Add(row);
        }

        _storage.SaveTable(_tableName, schema, rows);
        
        // Invalidate query cache on save
        if (_storage is BinaryStorage binaryStorage)
        {
            binaryStorage.QueryCache.InvalidateTable(_tableName);
        }
    }

    /// <summary>
    /// Convert value to target type
    /// </summary>
    private object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;

        if (targetType.IsAssignableFrom(value.GetType()))
        {
            return value;
        }

        // Conversion via JsonElement (if value from JSON)
        if (value is JsonElement jsonElement)
        {
            return JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType);
        }

        return Convert.ChangeType(value, targetType);
    }

    /// <summary>
    /// Ensure data is loaded
    /// </summary>
    private void EnsureLoaded()
    {
        if (!_isLoaded)
        {
            Load();
        }
    }

    /// <summary>
    /// Get all records
    /// </summary>
    public List<T> GetAll()
    {
        EnsureLoaded();
        return _data.ToList();
    }

    /// <summary>
    /// Get record by primary key
    /// </summary>
    public T? GetById(object id)
    {
        EnsureLoaded();

        var primaryKeyColumn = _columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (primaryKeyColumn == null) return null;

        var prop = _propertyMap.GetValueOrDefault(primaryKeyColumn.Name);
        if (prop == null) return null;

        return _data.FirstOrDefault(e => Equals(prop.GetValue(e), id));
    }
}
