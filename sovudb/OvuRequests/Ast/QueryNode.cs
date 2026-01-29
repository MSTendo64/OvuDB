using ovudb.Query;
using ovudb.SystemDatabase.Models;

namespace ovudb.OvuRequests.Ast;

/// <summary>
/// Base class for all query AST nodes
/// </summary>
public abstract class QueryNode
{
}

/// <summary>
/// SELECT query node
/// </summary>
public class SelectNode : QueryNode
{
    public List<string> Columns { get; set; } = new(); // "*" or column list
    public string TableName { get; set; } = string.Empty;
    public WhereNode? Where { get; set; }
    public OrderByNode? OrderBy { get; set; }
    public LimitNode? Limit { get; set; }
    public OffsetNode? Offset { get; set; }
    public GroupByNode? GroupBy { get; set; }
    public List<AggregateFunction> AggregateFunctions { get; set; } = new();
}

/// <summary>
/// INSERT query node
/// </summary>
public class InsertNode : QueryNode
{
    public string TableName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public List<List<object?>> Values { get; set; } = new();
}

/// <summary>
/// UPDATE query node
/// </summary>
public class UpdateNode : QueryNode
{
    public string TableName { get; set; } = string.Empty;
    public Dictionary<string, object?> SetValues { get; set; } = new();
    public WhereNode? Where { get; set; }
}

/// <summary>
/// DELETE query node
/// </summary>
public class DeleteNode : QueryNode
{
    public string TableName { get; set; } = string.Empty;
    public WhereNode? Where { get; set; }
}

/// <summary>
/// MODEL ADD command node
/// </summary>
public class ModelAddNode : QueryNode
{
    public string ModelName { get; set; } = string.Empty;
    public List<ModelField> Fields { get; set; } = new();
    public string ModelType { get; set; } = "perm";
}

/// <summary>
/// MODEL LIST command node
/// </summary>
public class ModelListNode : QueryNode
{
}

/// <summary>
/// MODEL EDIT command node
/// </summary>
public class ModelEditNode : QueryNode
{
    public string ModelName { get; set; } = string.Empty;
    public List<ModelField> OldFields { get; set; } = new();
    public List<ModelField> NewFields { get; set; } = new();
}

/// <summary>
/// MODEL SEE command node
/// </summary>
public class ModelSeeNode : QueryNode
{
    public string ModelName { get; set; } = string.Empty;
}

/// <summary>
/// MODEL DEL command node
/// </summary>
public class ModelDelNode : QueryNode
{
    public string ModelName { get; set; } = string.Empty;
    public List<string>? FieldNames { get; set; }
}

/// <summary>
/// MODEL USE command node
/// </summary>
public class ModelUseNode : QueryNode
{
    public string ModelName { get; set; } = string.Empty;
}

/// <summary>
/// CREATE TABLE command node
/// </summary>
public class CreateTableNode : QueryNode
{
    public string TableName { get; set; } = string.Empty;
    public List<CreateTableColumn> Columns { get; set; } = new();
}

/// <summary>
/// Column for CREATE TABLE
/// </summary>
public class CreateTableColumn
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsAutoIncrement { get; set; }
    public bool IsUnique { get; set; }
    public object? DefaultValue { get; set; }
}

/// <summary>
/// DROP TABLE command node
/// </summary>
public class DropTableNode : QueryNode
{
    public string TableName { get; set; } = string.Empty;
}

/// <summary>
/// WHERE condition node
/// </summary>
public class WhereNode : QueryNode
{
    public ConditionNode Condition { get; set; } = null!;
}

/// <summary>
/// Condition node (may be compound)
/// </summary>
public class ConditionNode : QueryNode
{
    public string? ColumnName { get; set; }
    public ComparisonOperator Operator { get; set; }
    public object? Value { get; set; }
    public List<object?>? Values { get; set; } // For IN/NOT IN
    public ConditionNode? Left { get; set; }
    public ConditionNode? Right { get; set; }
    public LogicalOperator LogicalOp { get; set; } = LogicalOperator.And;
    public bool IsNegated { get; set; }
}

/// <summary>
/// ORDER BY node
/// </summary>
public class OrderByNode : QueryNode
{
    public List<OrderByItem> Items { get; set; } = new();
}

/// <summary>
/// Sort item
/// </summary>
public class OrderByItem
{
    public string ColumnName { get; set; } = string.Empty;
    public bool Descending { get; set; }
}

/// <summary>
/// LIMIT node
/// </summary>
public class LimitNode : QueryNode
{
    public int Count { get; set; }
}

/// <summary>
/// OFFSET node
/// </summary>
public class OffsetNode : QueryNode
{
    public int Count { get; set; }
}

/// <summary>
/// GROUP BY node
/// </summary>
public class GroupByNode : QueryNode
{
    public List<string> Columns { get; set; } = new();
}

/// <summary>
/// Aggregate function
/// </summary>
public class AggregateFunction
{
    public AggregateType Type { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string? Alias { get; set; }
}

/// <summary>
/// Aggregate function type
/// </summary>
public enum AggregateType
{
    Count,
    Sum,
    Avg,
    Min,
    Max
}

/// <summary>
/// Logical operator
/// </summary>
public enum LogicalOperator
{
    And,
    Or
}
