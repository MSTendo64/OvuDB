using ovudb.Core;
using ovudb.OvuRequests.Ast;

namespace ovudb.OvuRequests;

/// <summary>
/// Query optimizer for ovuRequests
/// Performs query optimization for better performance
/// </summary>
public class Optimizer
{
    private readonly Database _database;

    public Optimizer(Database database)
    {
        _database = database;
    }

    /// <summary>
    /// Optimize query
    /// </summary>
    public QueryNode Optimize(QueryNode query)
    {
        return query switch
        {
            SelectNode select => OptimizeSelect(select),
            InsertNode insert => insert, // INSERT usually does not need optimization
            UpdateNode update => OptimizeUpdate(update),
            DeleteNode delete => OptimizeDelete(delete),
            _ => query
        };
    }

    /// <summary>
    /// Optimize SELECT query
    /// </summary>
    private SelectNode OptimizeSelect(SelectNode node)
    {
        // Optimize WHERE conditions
        if (node.Where != null)
        {
            node.Where.Condition = OptimizeCondition(node.Where.Condition, node.TableName);
        }

        // Reorder WHERE conditions for index usage
        if (node.Where != null)
        {
            node.Where.Condition = ReorderConditions(node.Where.Condition, node.TableName);
        }

        // Optimize ORDER BY - if index exists, use it
        if (node.OrderBy != null && node.OrderBy.Items.Count > 0)
        {
            var firstOrderColumn = node.OrderBy.Items[0].ColumnName;
            if (HasIndex(node.TableName, firstOrderColumn))
            {
                // Index can help with sorting
                // In future can add optimizer hints
            }
        }

        // Optimize LIMIT/OFFSET - apply as early as possible
        // If WHERE with index exists, can apply LIMIT earlier

        return node;
    }

    /// <summary>
    /// Optimize UPDATE query
    /// </summary>
    private UpdateNode OptimizeUpdate(UpdateNode node)
    {
        // Optimize WHERE conditions
        if (node.Where != null)
        {
            node.Where.Condition = OptimizeCondition(node.Where.Condition, node.TableName);
            node.Where.Condition = ReorderConditions(node.Where.Condition, node.TableName);
        }

        return node;
    }

    /// <summary>
    /// Optimize DELETE query
    /// </summary>
    private DeleteNode OptimizeDelete(DeleteNode node)
    {
        // Optimize WHERE conditions
        if (node.Where != null)
        {
            node.Where.Condition = OptimizeCondition(node.Where.Condition, node.TableName);
            node.Where.Condition = ReorderConditions(node.Where.Condition, node.TableName);
        }

        return node;
    }

    /// <summary>
    /// Optimize condition
    /// </summary>
    private ConditionNode OptimizeCondition(ConditionNode condition, string tableName)
    {
        // Simplify conditions
        if (condition.Left != null && condition.Right != null)
        {
            // Recursively optimize left and right parts
            condition.Left = OptimizeCondition(condition.Left, tableName);
            condition.Right = OptimizeCondition(condition.Right, tableName);

            // If both parts equal, simplify
            if (AreConditionsEqual(condition.Left, condition.Right))
            {
                return condition.Left;
            }

            // If one part is always true/false, simplify
            if (IsAlwaysTrue(condition.Left))
            {
                return condition.LogicalOp == LogicalOperator.And ? condition.Right : condition.Left;
            }
            if (IsAlwaysFalse(condition.Left))
            {
                return condition.LogicalOp == LogicalOperator.And ? condition.Left : condition.Right;
            }
            if (IsAlwaysTrue(condition.Right))
            {
                return condition.LogicalOp == LogicalOperator.And ? condition.Left : condition.Right;
            }
            if (IsAlwaysFalse(condition.Right))
            {
                return condition.LogicalOp == LogicalOperator.And ? condition.Right : condition.Left;
            }
        }

        // Optimize simple conditions
        if (condition.ColumnName != null)
        {
            // If condition uses index, mark as priority
            if (HasIndex(tableName, condition.ColumnName))
            {
                // In future can add priority flag
            }
        }

        return condition;
    }

    /// <summary>
    /// Reorder conditions for index usage
    /// </summary>
    private ConditionNode ReorderConditions(ConditionNode condition, string tableName)
    {
        if (condition.Left == null || condition.Right == null)
        {
            return condition;
        }

        // Recursively reorder
        condition.Left = ReorderConditions(condition.Left, tableName);
        condition.Right = ReorderConditions(condition.Right, tableName);

        // If right part uses index and left does not, swap
        var leftHasIndex = condition.Left.ColumnName != null && HasIndex(tableName, condition.Left.ColumnName);
        var rightHasIndex = condition.Right.ColumnName != null && HasIndex(tableName, condition.Right.ColumnName);

        if (!leftHasIndex && rightHasIndex && condition.LogicalOp == LogicalOperator.And)
        {
            // Swap to use index earlier
            (condition.Left, condition.Right) = (condition.Right, condition.Left);
        }

        return condition;
    }

    /// <summary>
    /// Check if column has index
    /// </summary>
    private bool HasIndex(string tableName, string columnName)
    {
        try
        {
            if (!_database.TableExists(tableName))
            {
                return false;
            }

            // In future can add index check via metadata
            // For now return false
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if conditions are equal
    /// </summary>
    private bool AreConditionsEqual(ConditionNode left, ConditionNode right)
    {
        if (left.ColumnName != right.ColumnName)
            return false;
        if (left.Operator != right.Operator)
            return false;
        if (!Equals(left.Value, right.Value))
            return false;
        if (left.IsNegated != right.IsNegated)
            return false;
        return true;
    }

    /// <summary>
    /// Check if condition is always true
    /// </summary>
    private bool IsAlwaysTrue(ConditionNode condition)
    {
        // Simple check - real implementation needs more complex logic
        return false;
    }

    /// <summary>
    /// Check if condition is always false
    /// </summary>
    private bool IsAlwaysFalse(ConditionNode condition)
    {
        // Simple check - real implementation needs more complex logic
        return false;
    }
}
