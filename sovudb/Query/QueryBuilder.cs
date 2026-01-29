using System.Linq.Expressions;
using System.Reflection;
using ovudb.Core;

namespace ovudb.Query;

/// <summary>
/// Query builder for flexible data access
/// </summary>
public class QueryBuilder<T> where T : class, new()
{
    private readonly Table<T> _table;
    private readonly List<WhereCondition> _whereConditions = new();
    private readonly List<string> _orderByColumns = new();
    private bool _orderByDescending = false;
    private int? _limitCount;
    private int? _offsetCount;
    private readonly List<string> _selectColumns = new();

    public QueryBuilder(Table<T> table)
    {
        _table = table;
    }

    /// <summary>
    /// Add WHERE condition
    /// </summary>
    public QueryBuilder<T> Where(string columnName, object value, ComparisonOperator op = ComparisonOperator.Equals)
    {
        _whereConditions.Add(new WhereCondition(columnName, value, op));
        return this;
    }

    /// <summary>
    /// Add WHERE condition with expression
    /// </summary>
    public QueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        var condition = ExpressionHelper.ConvertExpression<T>(predicate);
        _whereConditions.Add(condition);
        return this;
    }

    /// <summary>
    /// Add AND condition
    /// </summary>
    public QueryBuilder<T> And(string columnName, object value, ComparisonOperator op = ComparisonOperator.Equals)
    {
        return Where(columnName, value, op);
    }

    /// <summary>
    /// Add OR condition
    /// </summary>
    public QueryBuilder<T> Or(string columnName, object value, ComparisonOperator op = ComparisonOperator.Equals)
    {
        var condition = new WhereCondition(columnName, value, op) { IsOr = true };
        _whereConditions.Add(condition);
        return this;
    }

    /// <summary>
    /// Order ascending
    /// </summary>
    public QueryBuilder<T> OrderBy(string columnName)
    {
        _orderByColumns.Clear();
        _orderByColumns.Add(columnName);
        _orderByDescending = false;
        return this;
    }

    /// <summary>
    /// Order descending
    /// </summary>
    public QueryBuilder<T> OrderByDescending(string columnName)
    {
        _orderByColumns.Clear();
        _orderByColumns.Add(columnName);
        _orderByDescending = true;
        return this;
    }

    /// <summary>
    /// Limit number of results
    /// </summary>
    public QueryBuilder<T> Limit(int count)
    {
        _limitCount = count;
        return this;
    }

    /// <summary>
    /// Skip number of records
    /// </summary>
    public QueryBuilder<T> Offset(int count)
    {
        _offsetCount = count;
        return this;
    }

    /// <summary>
    /// Select specific columns
    /// </summary>
    public QueryBuilder<T> Select(params string[] columnNames)
    {
        _selectColumns.AddRange(columnNames);
        return this;
    }

    /// <summary>
    /// Execute query and get results
    /// </summary>
    public List<T> ToList()
    {
        return _table.ExecuteQuery(this);
    }

    /// <summary>
    /// Get first result
    /// </summary>
    public T? FirstOrDefault()
    {
        return ToList().FirstOrDefault();
    }

    /// <summary>
    /// Get single result
    /// </summary>
    public T? SingleOrDefault()
    {
        return ToList().SingleOrDefault();
    }

    /// <summary>
    /// Count records
    /// </summary>
    public int Count()
    {
        return _table.ExecuteCount(this);
    }

    /// <summary>
    /// Check if any records exist
    /// </summary>
    public bool Any()
    {
        return Count() > 0;
    }

    internal List<WhereCondition> WhereConditions => _whereConditions;
    internal List<string> OrderByColumns => _orderByColumns;
    internal bool IsOrderByDescending => _orderByDescending;
    internal int? LimitCount => _limitCount;
    internal int? OffsetCount => _offsetCount;
    internal List<string> SelectColumns => _selectColumns;

    /// <summary>
    /// Get string representation of query for caching
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();
        
        if (_whereConditions.Any())
        {
            var whereStr = string.Join(" AND ", _whereConditions.Select(c => 
                $"{c.ColumnName}{GetOperatorSymbol(c.Operator)}{c.Value}"));
            parts.Add($"WHERE {whereStr}");
        }
        
        if (_orderByColumns.Any())
        {
            var orderStr = string.Join(", ", _orderByColumns);
            parts.Add($"ORDER BY {orderStr} {(_orderByDescending ? "DESC" : "ASC")}");
        }
        
        if (_limitCount.HasValue)
        {
            parts.Add($"LIMIT {_limitCount.Value}");
        }
        
        if (_offsetCount.HasValue)
        {
            parts.Add($"OFFSET {_offsetCount.Value}");
        }
        
        return string.Join(" ", parts);
    }

    private static string GetOperatorSymbol(ComparisonOperator op)
    {
        return op switch
        {
            ComparisonOperator.Equals => "=",
            ComparisonOperator.NotEquals => "!=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            ComparisonOperator.Like => " LIKE ",
            ComparisonOperator.In => " IN ",
            ComparisonOperator.NotIn => " NOT IN ",
            _ => "="
        };
    }
}

/// <summary>
/// Comparison operators
/// </summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like,
    In,
    NotIn
}

/// <summary>
/// WHERE condition
/// </summary>
public class WhereCondition
{
    public string ColumnName { get; set; }
    public object? Value { get; set; }
    public ComparisonOperator Operator { get; set; }
    public bool IsOr { get; set; }

    public WhereCondition(string columnName, object? value, ComparisonOperator op)
    {
        ColumnName = columnName;
        Value = value;
        Operator = op;
    }
}

/// <summary>
/// Helper class for expression handling
/// </summary>
internal static class ExpressionHelper
{
    public static WhereCondition ConvertExpression<T>(Expression<Func<T, bool>> predicate) where T : class
    {
        // Simplified implementation - real project would need a more complex parser
        // Using simple approach for demonstration
        if (predicate.Body is BinaryExpression binary)
        {
            var left = binary.Left as MemberExpression;
            var right = binary.Right as ConstantExpression;

            if (left != null && right != null)
            {
                var columnName = left.Member.Name;
                var value = right.Value;
                var op = binary.NodeType switch
                {
                    ExpressionType.Equal => ComparisonOperator.Equals,
                    ExpressionType.NotEqual => ComparisonOperator.NotEquals,
                    ExpressionType.GreaterThan => ComparisonOperator.GreaterThan,
                    ExpressionType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
                    ExpressionType.LessThan => ComparisonOperator.LessThan,
                    ExpressionType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
                    _ => ComparisonOperator.Equals
                };

                return new WhereCondition(columnName, value, op);
            }
        }

        throw new NotSupportedException("Unsupported expression");
    }
}
