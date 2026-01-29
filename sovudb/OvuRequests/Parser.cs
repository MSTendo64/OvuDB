using System.Text;
using System.Text.RegularExpressions;
using ovudb.OvuRequests.Ast;
using ovudb.Query;
using ovudb.SystemDatabase.Models;

namespace ovudb.OvuRequests;

/// <summary>
/// Parser for ovuRequests query language. Supports SQL-like syntax.
/// </summary>
public class Parser
{
    private readonly string _query;
    private int _position;
    private readonly List<Token> _tokens = new();
    private int _tokenIndex;

    public Parser(string query)
    {
        _query = query.Trim();
        Tokenize();
    }

    /// <summary>
    /// Parse query into AST
    /// </summary>
    public QueryNode Parse()
    {
        if (_tokens.Count == 0)
        {
            throw new ArgumentException("Empty query");
        }

        _tokenIndex = 0;
        var token = PeekToken();

        return token.Type switch
        {
            TokenType.Select => ParseSelect(),
            TokenType.Insert => ParseInsert(),
            TokenType.Update => ParseUpdate(),
            TokenType.Delete => ParseDelete(),
            TokenType.Model => ParseModel(),
            TokenType.Create => ParseCreate(),
            TokenType.Drop => ParseDrop(),
            _ => throw new ArgumentException($"Unknown command: {token.Value}")
        };
    }

    /// <summary>
    /// Parse SELECT query
    /// </summary>
    private SelectNode ParseSelect()
    {
        var node = new SelectNode();

        // SELECT
        ExpectToken(TokenType.Select);

        // Column list or *
        if (PeekToken().Type == TokenType.Asterisk)
        {
            ConsumeToken();
            node.Columns.Add("*");
        }
        else
        {
            // Parse column list or aggregate functions
            while (true)
            {
                var token = PeekToken();
                
                // Check for aggregate function
                if (IsAggregateFunction(token))
                {
                    node.AggregateFunctions.Add(ParseAggregateFunction());
                }
                else
                {
                    var columnName = ParseColumnName();
                    // Check for alias (AS)
                    if (PeekToken().Type == TokenType.As)
                    {
                        ConsumeToken(); // AS
                        var alias = ParseIdentifier();
                        node.Columns.Add(columnName);
                    }
                    else
                    {
                        node.Columns.Add(columnName);
                    }
                }

                if (PeekToken().Type == TokenType.Comma)
                {
                    ConsumeToken();
                    continue;
                }
                break;
            }
        }

        // FROM
        ExpectToken(TokenType.From);
        node.TableName = ParseIdentifier();

        // WHERE (optional)
        if (PeekToken().Type == TokenType.Where)
        {
            node.Where = ParseWhere();
        }

        // GROUP BY (optional)
        if (PeekToken().Type == TokenType.Group && PeekToken(1).Type == TokenType.By)
        {
            ConsumeToken(); // GROUP
            ConsumeToken(); // BY
            node.GroupBy = ParseGroupBy();
        }

        // ORDER BY (optional)
        if (PeekToken().Type == TokenType.Order && PeekToken(1).Type == TokenType.By)
        {
            ConsumeToken(); // ORDER
            ConsumeToken(); // BY
            node.OrderBy = ParseOrderBy();
        }

        // LIMIT (optional)
        if (PeekToken().Type == TokenType.Limit)
        {
            node.Limit = ParseLimit();
        }

        // OFFSET (optional)
        if (PeekToken().Type == TokenType.Offset)
        {
            node.Offset = ParseOffset();
        }

        return node;
    }

    /// <summary>
    /// Parse INSERT query
    /// </summary>
    private InsertNode ParseInsert()
    {
        var node = new InsertNode();

        // INSERT INTO
        ExpectToken(TokenType.Insert);
        ExpectToken(TokenType.Into);
        node.TableName = ParseIdentifier();

        // Column list (optional)
        if (PeekToken().Type == TokenType.LeftParen)
        {
            ConsumeToken();
            while (PeekToken().Type != TokenType.RightParen)
            {
                node.Columns.Add(ParseIdentifier());
                if (PeekToken().Type == TokenType.Comma)
                {
                    ConsumeToken();
                }
            }
            ConsumeToken(); // )
        }

        // VALUES
        ExpectToken(TokenType.Values);

        // Value list
        while (true)
        {
            ExpectToken(TokenType.LeftParen);
            var values = new List<object?>();
            while (PeekToken().Type != TokenType.RightParen)
            {
                values.Add(ParseValue());
                if (PeekToken().Type == TokenType.Comma)
                {
                    ConsumeToken();
                }
            }
            ConsumeToken(); // )
            node.Values.Add(values);

            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }

        return node;
    }

    /// <summary>
    /// Parse UPDATE query
    /// </summary>
    private UpdateNode ParseUpdate()
    {
        var node = new UpdateNode();

        // UPDATE
        ExpectToken(TokenType.Update);
        node.TableName = ParseIdentifier();

        // SET
        ExpectToken(TokenType.Set);

        // Assignment list
        while (true)
        {
            var column = ParseIdentifier();
            ExpectToken(TokenType.Equals);
            // Parse value (literal or expression like column + 1)
            var value = ParseValueOrExpression();
            node.SetValues[column] = value;

            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }

        // WHERE (optional)
        if (PeekToken().Type == TokenType.Where)
        {
            node.Where = ParseWhere();
        }

        return node;
    }

    /// <summary>
    /// Parse DELETE query
    /// </summary>
    private DeleteNode ParseDelete()
    {
        var node = new DeleteNode();

        // DELETE FROM
        ExpectToken(TokenType.Delete);
        ExpectToken(TokenType.From);
        node.TableName = ParseIdentifier();

        // WHERE (optional)
        if (PeekToken().Type == TokenType.Where)
        {
            node.Where = ParseWhere();
        }

        return node;
    }

    /// <summary>
    /// Parse CREATE command
    /// </summary>
    private QueryNode ParseCreate()
    {
        ExpectToken(TokenType.Create);
        
        if (PeekToken().Type == TokenType.Table)
        {
            return ParseCreateTable();
        }
        
        throw new ArgumentException("CREATE supports only TABLE");
    }

    /// <summary>
    /// Parse CREATE TABLE
    /// </summary>
    private QueryNode ParseCreateTable()
    {
        ExpectToken(TokenType.Table);
        var tableName = ParseIdentifier();
        
        ExpectToken(TokenType.LeftParen);
        
        var columns = new List<CreateTableColumn>();
        
        while (true)
        {
            var columnName = ParseIdentifier();
            var dataType = ParseIdentifier(); // INTEGER, STRING, etc.
            
            var column = new CreateTableColumn
            {
                Name = columnName,
                DataType = dataType
            };
            
            // Optional modifiers
            while (true)
            {
                var token = PeekToken();
                if (token.Type == TokenType.Primary)
                {
                    ConsumeToken();
                    ExpectToken(TokenType.Key);
                    column.IsPrimaryKey = true;
                    column.IsNullable = false;
                }
                else if (token.Type == TokenType.AutoIncrement)
                {
                    ConsumeToken();
                    column.IsAutoIncrement = true;
                }
                else if (token.Type == TokenType.Not)
                {
                    ConsumeToken();
                    if (PeekToken().Type == TokenType.Null)
                    {
                        ConsumeToken();
                        column.IsNullable = false;
                    }
                    else
                    {
                        throw new ArgumentException($"Expected NULL after NOT, got: {PeekToken().Value}");
                    }
                }
                else if (token.Type == TokenType.Unique)
                {
                    ConsumeToken();
                    column.IsUnique = true;
                }
                else if (token.Type == TokenType.Default)
                {
                    ConsumeToken();
                    column.DefaultValue = ParseValue();
                }
                else
                {
                    break;
                }
            }
            
            columns.Add(column);
            
            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }
        
        ExpectToken(TokenType.RightParen);
        
        // Validate: only one column may have PRIMARY KEY
        var primaryKeyCount = columns.Count(c => c.IsPrimaryKey);
        if (primaryKeyCount > 1)
        {
            throw new ArgumentException("Table can have only one PRIMARY KEY");
        }
        
        return new CreateTableNode
        {
            TableName = tableName,
            Columns = columns
        };
    }

    /// <summary>
    /// Parse DROP command
    /// </summary>
    private QueryNode ParseDrop()
    {
        ExpectToken(TokenType.Drop);
        
        if (PeekToken().Type == TokenType.Table)
        {
            ExpectToken(TokenType.Table);
            var tableName = ParseIdentifier();
            return new DropTableNode { TableName = tableName };
        }
        
        throw new ArgumentException("DROP supports only TABLE");
    }

    /// <summary>
    /// Parse MODEL command
    /// </summary>
    private QueryNode ParseModel()
    {
        ExpectToken(TokenType.Model);
        var command = PeekToken();

        return command.Type switch
        {
            TokenType.Add => ParseModelAdd(),
            TokenType.List => ParseModelList(),
            TokenType.Edit => ParseModelEdit(),
            TokenType.See => ParseModelSee(),
            TokenType.Del => ParseModelDel(),
            TokenType.Use => ParseModelUse(),
            _ => throw new ArgumentException($"Unknown MODEL command: {command.Value}")
        };
    }

    /// <summary>
    /// Parse MODEL ADD
    /// </summary>
    private ModelAddNode ParseModelAdd()
    {
        var node = new ModelAddNode();

        // MODEL ADD name
        ExpectToken(TokenType.Add);
        node.ModelName = ParseIdentifier();

        // Parse field list in braces
        ExpectToken(TokenType.LeftBrace);
        node.Fields = ParseModelFields();
        ExpectToken(TokenType.RightBrace);

        // Model type (perm or temp) - optional, default perm
        if (PeekToken().Type == TokenType.LeftParen)
        {
            ConsumeToken(); // (
            var typeToken = ConsumeToken();
            if (typeToken.Type == TokenType.Perm || typeToken.Type == TokenType.Temp)
            {
                node.ModelType = typeToken.Type == TokenType.Perm ? "perm" : "temp";
            }
            else
            {
                throw new ArgumentException($"Expected model type (perm or temp), got: {typeToken.Value}");
            }
            ExpectToken(TokenType.RightParen); // )
        }

        return node;
    }

    /// <summary>
    /// Parse MODEL LIST
    /// </summary>
    private ModelListNode ParseModelList()
    {
        ExpectToken(TokenType.List);
        return new ModelListNode();
    }

    /// <summary>
    /// Parse MODEL EDIT
    /// </summary>
    private ModelEditNode ParseModelEdit()
    {
        var node = new ModelEditNode();

        // MODEL EDIT name
        ExpectToken(TokenType.Edit);
        node.ModelName = ParseIdentifier();

        // Old fields
        ExpectToken(TokenType.LeftBrace);
        node.OldFields = ParseModelFields();
        ExpectToken(TokenType.RightBrace);

        // New fields
        ExpectToken(TokenType.LeftBrace);
        node.NewFields = ParseModelFields();
        ExpectToken(TokenType.RightBrace);

        return node;
    }

    /// <summary>
    /// Parse MODEL SEE
    /// </summary>
    private ModelSeeNode ParseModelSee()
    {
        ExpectToken(TokenType.See);
        var node = new ModelSeeNode
        {
            ModelName = ParseIdentifier()
        };
        return node;
    }

    /// <summary>
    /// Parse MODEL DEL
    /// </summary>
    private ModelDelNode ParseModelDel()
    {
        var node = new ModelDelNode();

        // MODEL DEL name
        ExpectToken(TokenType.Del);
        node.ModelName = ParseIdentifier();

        // If braces present, parse field list to remove
        if (PeekToken().Type == TokenType.LeftBrace)
        {
            ConsumeToken();
            node.FieldNames = ParseFieldNames();
            ExpectToken(TokenType.RightBrace);
        }

        return node;
    }

    /// <summary>
    /// Parse MODEL USE
    /// </summary>
    private ModelUseNode ParseModelUse()
    {
        ExpectToken(TokenType.Use);
        var node = new ModelUseNode
        {
            ModelName = ParseIdentifier()
        };
        return node;
    }

    /// <summary>
    /// Parse model field list. Format: name:type:key or name:type
    /// </summary>
    private List<ModelField> ParseModelFields()
    {
        var fields = new List<ModelField>();

        while (true)
        {
            var fieldName = ParseIdentifier();
            ExpectToken(TokenType.Colon);
            var fieldType = ParseIdentifier();
            
            var field = new ModelField
            {
                Name = fieldName,
                Type = fieldType
            };

            // Check for second colon and "key" keyword
            if (PeekToken().Type == TokenType.Colon)
            {
                ConsumeToken(); // :
                var keyToken = ConsumeToken();
                if (keyToken.Type == TokenType.Key)
                {
                    field.IsKey = true;
                }
                else
                {
                    throw new ArgumentException($"Expected 'key' after second colon, got: {keyToken.Value}");
                }
            }

            fields.Add(field);

            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }

        return fields;
    }

    /// <summary>
    /// Parse list of field names
    /// </summary>
    private List<string> ParseFieldNames()
    {
        var names = new List<string>();

        while (true)
        {
            names.Add(ParseIdentifier());

            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }

        return names;
    }

    /// <summary>
    /// Parse WHERE condition
    /// </summary>
    private WhereNode ParseWhere()
    {
        ConsumeToken(); // WHERE
        return new WhereNode
        {
            Condition = ParseCondition()
        };
    }

    /// <summary>
    /// Parse condition (recursive for AND/OR)
    /// </summary>
    private ConditionNode ParseCondition()
    {
        ConditionNode left;
        
        // Check if condition starts with parenthesis
        if (PeekToken().Type == TokenType.LeftParen)
        {
            ConsumeToken(); // (
            left = ParseCondition();
            ExpectToken(TokenType.RightParen); // )
        }
        else
        {
            left = ParseSimpleCondition();
        }

        while (true)
        {
            var token = PeekToken();
            if (token.Type == TokenType.And || token.Type == TokenType.Or)
            {
                var op = token.Type == TokenType.And ? LogicalOperator.And : LogicalOperator.Or;
                ConsumeToken();
                
                ConditionNode right;
                // Check if right part starts with parenthesis
                if (PeekToken().Type == TokenType.LeftParen)
                {
                    ConsumeToken(); // (
                    right = ParseCondition();
                    ExpectToken(TokenType.RightParen); // )
                }
                else
                {
                    right = ParseSimpleCondition();
                }
                
                left = new ConditionNode
                {
                    Left = left,
                    Right = right,
                    LogicalOp = op
                };
            }
            else
            {
                break;
            }
        }

        return left;
    }

    /// <summary>
    /// Parse simple condition
    /// </summary>
    private ConditionNode ParseSimpleCondition()
    {
        // NOT (optional at start)
        bool isNegated = false;
        if (PeekToken().Type == TokenType.Not)
        {
            ConsumeToken();
            // Check for NOT IN
            if (PeekToken().Type == TokenType.In)
            {
                ConsumeToken(); // IN
                var notInColumnName = ParseIdentifier();
                ExpectToken(TokenType.LeftParen);
                var notInValues = new List<object?>();
                while (PeekToken().Type != TokenType.RightParen)
                {
                    notInValues.Add(ParseValue());
                    if (PeekToken().Type == TokenType.Comma)
                    {
                        ConsumeToken();
                    }
                }
                ConsumeToken(); // )
                
                return new ConditionNode
                {
                    ColumnName = notInColumnName,
                    Operator = ComparisonOperator.NotIn,
                    Values = notInValues
                };
            }
            isNegated = true;
        }

        var columnName = ParseIdentifier();
        
        // Check for IS NULL or IS NOT NULL
        if (PeekToken().Type == TokenType.Identifier && PeekToken().Value.ToUpperInvariant() == "IS")
        {
            ConsumeToken(); // IS
            if (PeekToken().Type == TokenType.Not)
            {
                ConsumeToken(); // NOT
                ExpectToken(TokenType.Null);
                return new ConditionNode
                {
                    ColumnName = columnName,
                    Operator = ComparisonOperator.NotEquals,
                    Value = null
                };
            }
            else if (PeekToken().Type == TokenType.Null)
            {
                ConsumeToken(); // NULL
                return new ConditionNode
                {
                    ColumnName = columnName,
                    Operator = ComparisonOperator.Equals,
                    Value = null
                };
            }
        }
        
        // Check for NOT IN (after column name)
        if (PeekToken().Type == TokenType.Not && PeekToken(1).Type == TokenType.In)
        {
            ConsumeToken(); // NOT
            ConsumeToken(); // IN
            ExpectToken(TokenType.LeftParen);
            var notInValues = new List<object?>();
            while (PeekToken().Type != TokenType.RightParen)
            {
                notInValues.Add(ParseValue());
                if (PeekToken().Type == TokenType.Comma)
                {
                    ConsumeToken();
                }
            }
            ConsumeToken(); // )
            
            return new ConditionNode
            {
                ColumnName = columnName,
                Operator = ComparisonOperator.NotIn,
                Values = notInValues
            };
        }
        
        var op = ParseComparisonOperator();
        object? value = null;
        List<object?>? values = null;

        if (op == ComparisonOperator.In || op == ComparisonOperator.NotIn)
        {
            ExpectToken(TokenType.LeftParen);
            values = new List<object?>();
            while (PeekToken().Type != TokenType.RightParen)
            {
                values.Add(ParseValue());
                if (PeekToken().Type == TokenType.Comma)
                {
                    ConsumeToken();
                }
            }
            ConsumeToken(); // )
        }
        else
        {
            value = ParseValue();
        }

        return new ConditionNode
        {
            ColumnName = columnName,
            Operator = op,
            Value = value,
            Values = values,
            IsNegated = isNegated
        };
    }

    /// <summary>
    /// Parse comparison operator
    /// </summary>
    private ComparisonOperator ParseComparisonOperator()
    {
        var token = ConsumeToken();
        return token.Type switch
        {
            TokenType.Equals => ComparisonOperator.Equals,
            TokenType.NotEquals => ComparisonOperator.NotEquals,
            TokenType.GreaterThan => ComparisonOperator.GreaterThan,
            TokenType.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
            TokenType.LessThan => ComparisonOperator.LessThan,
            TokenType.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
            TokenType.Like => ComparisonOperator.Like,
            TokenType.In => ComparisonOperator.In,
            TokenType.NotIn => ComparisonOperator.NotIn,
            _ => throw new ArgumentException($"Unexpected operator: {token.Value}")
        };
    }

    /// <summary>
    /// Parse ORDER BY
    /// </summary>
    private OrderByNode ParseOrderBy()
    {
        var node = new OrderByNode();

        while (true)
        {
            var column = ParseIdentifier();
            var descending = false;

            if (PeekToken().Type == TokenType.Desc)
            {
                ConsumeToken();
                descending = true;
            }
            else if (PeekToken().Type == TokenType.Asc)
            {
                ConsumeToken();
            }

            node.Items.Add(new OrderByItem
            {
                ColumnName = column,
                Descending = descending
            });

            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }

        return node;
    }

    /// <summary>
    /// Parse GROUP BY
    /// </summary>
    private GroupByNode ParseGroupBy()
    {
        var node = new GroupByNode();

        while (true)
        {
            node.Columns.Add(ParseIdentifier());
            if (PeekToken().Type == TokenType.Comma)
            {
                ConsumeToken();
                continue;
            }
            break;
        }

        return node;
    }

    /// <summary>
    /// Parse LIMIT
    /// </summary>
    private LimitNode ParseLimit()
    {
        ConsumeToken(); // LIMIT
        var count = int.Parse(ConsumeToken().Value);
        return new LimitNode { Count = count };
    }

    /// <summary>
    /// Parse OFFSET
    /// </summary>
    private OffsetNode ParseOffset()
    {
        ConsumeToken(); // OFFSET
        var count = int.Parse(ConsumeToken().Value);
        return new OffsetNode { Count = count };
    }

    /// <summary>
    /// Parse aggregate function
    /// </summary>
    private AggregateFunction ParseAggregateFunction()
    {
        var token = ConsumeToken();
        var funcType = token.Type switch
        {
            TokenType.Count => AggregateType.Count,
            TokenType.Sum => AggregateType.Sum,
            TokenType.Avg => AggregateType.Avg,
            TokenType.Min => AggregateType.Min,
            TokenType.Max => AggregateType.Max,
            _ => throw new ArgumentException($"Unknown aggregate function: {token.Value}")
        };

        ExpectToken(TokenType.LeftParen);
        
        // Check for * (e.g. COUNT(*))
        string column;
        if (PeekToken().Type == TokenType.Asterisk)
        {
            ConsumeToken();
            column = "*";
        }
        else
        {
            column = ParseColumnName();
        }
        
        ExpectToken(TokenType.RightParen);

        string? alias = null;
        if (PeekToken().Type == TokenType.As)
        {
            ConsumeToken();
            // After AS any name is allowed, including keywords
            var aliasToken = ConsumeToken();
            alias = aliasToken.Value;
        }

        return new AggregateFunction
        {
            Type = funcType,
            ColumnName = column,
            Alias = alias
        };
    }

    /// <summary>
    /// Parse column name
    /// </summary>
    private string ParseColumnName()
    {
        return ParseIdentifier();
    }

    /// <summary>
    /// Parse identifier
    /// </summary>
    private string ParseIdentifier()
    {
        var token = ConsumeToken();
        if (token.Type == TokenType.Identifier || token.Type == TokenType.String)
        {
            return token.Value;
        }
        throw new ArgumentException($"Expected identifier, got: {token.Value}");
    }

    /// <summary>
    /// Parse value
    /// </summary>
    private object? ParseValue()
    {
        var token = ConsumeToken();
        return token.Type switch
        {
            TokenType.Number => ParseNumber(token.Value),
            TokenType.String => token.Value.Trim('\'', '"'),
            TokenType.Null => null,
            TokenType.True => true,
            TokenType.False => false,
            _ => throw new ArgumentException($"Unexpected value: {token.Value}")
        };
    }

    /// <summary>
    /// Parse value or simple arithmetic expression (column + number or column - number)
    /// </summary>
    private object? ParseValueOrExpression()
    {
        var token = PeekToken();
        
        // If identifier (column), may be expression
        if (token.Type == TokenType.Identifier)
        {
            var columnName = token.Value;
            ConsumeToken();
            
            // Check for + or - operator
            if (PeekToken().Type == TokenType.Plus || PeekToken().Type == TokenType.Minus)
            {
                var op = ConsumeToken();
                var nextToken = PeekToken();
                
                // Must be number
                if (nextToken.Type == TokenType.Number)
                {
                    ConsumeToken();
                    var number = ParseNumber(nextToken.Value);
                    
                    // Return computed value as string for Executor
                    return $"{columnName}{op.Value}{nextToken.Value}";
                }
                else if (nextToken.Type == TokenType.Identifier)
                {
                    // May be column + column
                    ConsumeToken();
                    return $"{columnName}{op.Value}{nextToken.Value}";
                }
                else
                {
                    throw new ArgumentException($"Expected number or column after operator {op.Value}, got: {nextToken.Value}");
                }
            }
            else
            {
                // Plain column - return as string for Executor
                return columnName;
            }
        }
        
        // Ordinary value
        return ParseValue();
    }

    /// <summary>
    /// Parse number
    /// </summary>
    private object ParseNumber(string value)
    {
        if (value.Contains('.'))
        {
            return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        return int.Parse(value);
    }

    /// <summary>
    /// Check if token is aggregate function
    /// </summary>
    private bool IsAggregateFunction(Token token)
    {
        return token.Type == TokenType.Count ||
               token.Type == TokenType.Sum ||
               token.Type == TokenType.Avg ||
               token.Type == TokenType.Min ||
               token.Type == TokenType.Max;
    }

    /// <summary>
    /// Tokenize query
    /// </summary>
    private void Tokenize()
    {
        _position = 0;
        _tokens.Clear();

        while (_position < _query.Length)
        {
            SkipWhitespace();

            if (_position >= _query.Length)
                break;

            var ch = _query[_position];

            if (char.IsLetter(ch) || ch == '_')
            {
                ReadIdentifierOrKeyword();
            }
            else if (char.IsDigit(ch) || ch == '.' || ch == '-')
            {
                ReadNumber();
            }
            else if (ch == '\'' || ch == '"')
            {
                ReadString(ch);
            }
            else
            {
                ReadSymbol();
            }
        }
    }

    /// <summary>
    /// Skip whitespace
    /// </summary>
    private void SkipWhitespace()
    {
        while (_position < _query.Length && char.IsWhiteSpace(_query[_position]))
        {
            _position++;
        }
    }

    /// <summary>
    /// Read identifier or keyword
    /// </summary>
    private void ReadIdentifierOrKeyword()
    {
        var start = _position;
        while (_position < _query.Length && (char.IsLetterOrDigit(_query[_position]) || _query[_position] == '_'))
        {
            _position++;
        }

        var value = _query.Substring(start, _position - start);
        var upperValue = value.ToUpperInvariant();

        var tokenType = upperValue switch
        {
            "SELECT" => TokenType.Select,
            "FROM" => TokenType.From,
            "WHERE" => TokenType.Where,
            "INSERT" => TokenType.Insert,
            "INTO" => TokenType.Into,
            "VALUES" => TokenType.Values,
            "UPDATE" => TokenType.Update,
            "SET" => TokenType.Set,
            "DELETE" => TokenType.Delete,
            "ORDER" => TokenType.Order,
            "BY" => TokenType.By,
            "GROUP" => TokenType.Group,
            "LIMIT" => TokenType.Limit,
            "OFFSET" => TokenType.Offset,
            "AND" => TokenType.And,
            "OR" => TokenType.Or,
            "NOT" => TokenType.Not,
            "IN" => TokenType.In,
            "LIKE" => TokenType.Like,
            "ASC" => TokenType.Asc,
            "DESC" => TokenType.Desc,
            "AS" => TokenType.As,
            "COUNT" => TokenType.Count,
            "SUM" => TokenType.Sum,
            "AVG" => TokenType.Avg,
            "MIN" => TokenType.Min,
            "MAX" => TokenType.Max,
            "NULL" => TokenType.Null,
            "TRUE" => TokenType.True,
            "FALSE" => TokenType.False,
            "MODEL" => TokenType.Model,
            "ADD" => TokenType.Add,
            "LIST" => TokenType.List,
            "EDIT" => TokenType.Edit,
            "SEE" => TokenType.See,
            "DEL" => TokenType.Del,
            "USE" => TokenType.Use,
            "PERM" => TokenType.Perm,
            "TEMP" => TokenType.Temp,
            "CREATE" => TokenType.Create,
            "TABLE" => TokenType.Table,
            "DROP" => TokenType.Drop,
            "PRIMARY" => TokenType.Primary,
            "KEY" => TokenType.Key,
            "AUTOINCREMENT" => TokenType.AutoIncrement,
            "AUTO_INCREMENT" => TokenType.AutoIncrement,
            "UNIQUE" => TokenType.Unique,
            "DEFAULT" => TokenType.Default,
            _ => TokenType.Identifier
        };

        _tokens.Add(new Token(tokenType, value));
    }

    /// <summary>
    /// Read number
    /// </summary>
    private void ReadNumber()
    {
        var start = _position;
        if (_query[_position] == '-')
        {
            _position++;
        }

        while (_position < _query.Length && char.IsDigit(_query[_position]))
        {
            _position++;
        }

        if (_position < _query.Length && _query[_position] == '.')
        {
            _position++;
            while (_position < _query.Length && char.IsDigit(_query[_position]))
            {
                _position++;
            }
        }

        var value = _query.Substring(start, _position - start);
        _tokens.Add(new Token(TokenType.Number, value));
    }

    /// <summary>
    /// Read string
    /// </summary>
    private void ReadString(char quote)
    {
        _position++; // Skip opening quote
        var start = _position;
        var escaped = false;

        while (_position < _query.Length)
        {
            var ch = _query[_position];
            if (escaped)
            {
                escaped = false;
                _position++;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                _position++;
                continue;
            }

            if (ch == quote)
            {
                var value = _query.Substring(start, _position - start);
                _position++; // Skip closing quote
                _tokens.Add(new Token(TokenType.String, value));
                return;
            }

            _position++;
        }

        throw new ArgumentException("Unclosed string");
    }

    /// <summary>
    /// Read symbol
    /// </summary>
    private void ReadSymbol()
    {
        var ch = _query[_position];
        var nextCh = _position + 1 < _query.Length ? _query[_position + 1] : '\0';

        if (ch == '=' && nextCh == '=')
        {
            _position += 2;
            _tokens.Add(new Token(TokenType.Equals, "=="));
        }
        else if (ch == '!' && nextCh == '=')
        {
            _position += 2;
            _tokens.Add(new Token(TokenType.NotEquals, "!="));
        }
        else if (ch == '>' && nextCh == '=')
        {
            _position += 2;
            _tokens.Add(new Token(TokenType.GreaterThanOrEqual, ">="));
        }
        else if (ch == '<' && nextCh == '=')
        {
            _position += 2;
            _tokens.Add(new Token(TokenType.LessThanOrEqual, "<="));
        }
        else if (ch == '>')
        {
            _position++;
            _tokens.Add(new Token(TokenType.GreaterThan, ">"));
        }
        else if (ch == '<')
        {
            _position++;
            _tokens.Add(new Token(TokenType.LessThan, "<"));
        }
        else if (ch == '=')
        {
            _position++;
            _tokens.Add(new Token(TokenType.Equals, "="));
        }
        else if (ch == '(')
        {
            _position++;
            _tokens.Add(new Token(TokenType.LeftParen, "("));
        }
        else if (ch == ')')
        {
            _position++;
            _tokens.Add(new Token(TokenType.RightParen, ")"));
        }
        else if (ch == ',')
        {
            _position++;
            _tokens.Add(new Token(TokenType.Comma, ","));
        }
        else if (ch == '*')
        {
            _position++;
            _tokens.Add(new Token(TokenType.Asterisk, "*"));
        }
        else if (ch == '{')
        {
            _position++;
            _tokens.Add(new Token(TokenType.LeftBrace, "{"));
        }
        else if (ch == '}')
        {
            _position++;
            _tokens.Add(new Token(TokenType.RightBrace, "}"));
        }
        else if (ch == ':')
        {
            _position++;
            _tokens.Add(new Token(TokenType.Colon, ":"));
        }
        else if (ch == ';')
        {
            // Semicolon - command separator, ignore
            _position++;
        }
        else if (ch == '+')
        {
            _position++;
            _tokens.Add(new Token(TokenType.Plus, "+"));
        }
        else if (ch == '-')
        {
            _position++;
            _tokens.Add(new Token(TokenType.Minus, "-"));
        }
        else
        {
            throw new ArgumentException($"Unknown character: {ch}");
        }
    }

    /// <summary>
    /// Peek next token without consuming
    /// </summary>
    private Token PeekToken(int offset = 0)
    {
        if (_tokenIndex + offset >= _tokens.Count)
        {
            return new Token(TokenType.Eof, "");
        }
        return _tokens[_tokenIndex + offset];
    }

    /// <summary>
    /// Consume token
    /// </summary>
    private Token ConsumeToken()
    {
        if (_tokenIndex >= _tokens.Count)
        {
            return new Token(TokenType.Eof, "");
        }
        return _tokens[_tokenIndex++];
    }

    /// <summary>
    /// Expect specific token type
    /// </summary>
    private void ExpectToken(TokenType type)
    {
        var token = ConsumeToken();
        if (token.Type != type)
        {
            throw new ArgumentException($"Expected {type}, got {token.Type}: {token.Value}");
        }
    }
}

/// <summary>
/// Token
/// </summary>
internal class Token
{
    public TokenType Type { get; }
    public string Value { get; }

    public Token(TokenType type, string value)
    {
        Type = type;
        Value = value;
    }
}

/// <summary>
/// Token type
/// </summary>
internal enum TokenType
{
    // Keywords
    Select, From, Where, Insert, Into, Values, Update, Set, Delete,
    Order, By, Group, Limit, Offset, And, Or, Not, In, Like,
    Asc, Desc, As, Count, Sum, Avg, Min, Max, Null, True, False,
    Model, Add, List, Edit, See, Del, Use, Perm, Temp,
    Create, Table, Drop, Primary, Key, AutoIncrement, Unique, Default,

    // Operators
    Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
    NotIn, Plus, Minus,

    // Symbols
    LeftParen, RightParen, LeftBrace, RightBrace, Comma, Asterisk, Colon,

    // Literals
    Identifier, Number, String,

    // Special
    Eof
}
