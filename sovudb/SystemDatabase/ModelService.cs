using System.Text.Json;
using ovudb.Core;
using ovudb.SystemDatabase.Models;

namespace ovudb.SystemDatabase;

/// <summary>
/// Service for managing models (table templates)
/// </summary>
public class ModelService
{
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly Dictionary<string, Model> _tempModels = new(); // Temporary models in memory

    public ModelService(SystemDatabaseService systemDatabaseService)
    {
        _systemDatabaseService = systemDatabaseService;
    }

    /// <summary>
    /// Add model
    /// </summary>
    public bool AddModel(string name, List<ModelField> fields, string modelType = "perm")
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Model name cannot be empty");
        }

        if (fields == null || fields.Count == 0)
        {
            throw new ArgumentException("Model must contain at least one field");
        }

        if (modelType != "perm" && modelType != "temp")
        {
            throw new ArgumentException("Model type must be 'perm' or 'temp'");
        }

        // Check if model exists
        if (ModelExists(name))
        {
            return false;
        }

        // Validate field types
        foreach (var field in fields)
        {
            if (!IsValidDataType(field.Type))
            {
                throw new ArgumentException($"Unknown data type: {field.Type}");
            }
        }

        var fieldsJson = JsonSerializer.Serialize(fields);

        if (modelType == "temp")
        {
            // Temporary model - store in memory
            _tempModels[name] = new Model
            {
                Name = name,
                ModelType = "temp",
                FieldsJson = fieldsJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }
        else
        {
            // Permanent model - store in DB
            var modelTable = _systemDatabaseService.GetModelTable();
            var model = new Model
            {
                Name = name,
                ModelType = "perm",
                FieldsJson = fieldsJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            modelTable.Insert(model);
        }

        return true;
    }

    /// <summary>
    /// Check if data type is valid
    /// </summary>
    private bool IsValidDataType(string type)
    {
        var upperType = type.ToUpperInvariant();
        return upperType == "INTEGER" || upperType == "LONG" || upperType == "DOUBLE" ||
               upperType == "STRING" || upperType == "BOOLEAN" || upperType == "DATETIME" ||
               upperType == "BINARY" || upperType == "INT" || upperType == "BOOL" ||
               upperType == "TEXT" || upperType == "DECIMAL";
    }

    /// <summary>
    /// Get list of all models
    /// </summary>
    public List<ModelInfo> ListModels()
    {
        var result = new List<ModelInfo>();

        // Permanent models from DB
        var modelTable = _systemDatabaseService.GetModelTable();
        var permModels = modelTable.GetAll();
        foreach (var model in permModels)
        {
            result.Add(new ModelInfo
            {
                Name = model.Name,
                Type = model.ModelType,
                FieldCount = GetModelFields(model).Count,
                CreatedAt = model.CreatedAt
            });
        }

        // Temporary models from memory
        foreach (var model in _tempModels.Values)
        {
            result.Add(new ModelInfo
            {
                Name = model.Name,
                Type = model.ModelType,
                FieldCount = GetModelFields(model).Count,
                CreatedAt = model.CreatedAt
            });
        }

        return result.OrderBy(m => m.Name).ToList();
    }

    /// <summary>
    /// Edit model
    /// </summary>
    public bool EditModel(string name, List<ModelField> oldFields, List<ModelField> newFields)
    {
        if (oldFields == null || newFields == null)
        {
            throw new ArgumentException("Field lists cannot be null");
        }

        if (oldFields.Count != newFields.Count)
        {
            throw new ArgumentException("Count of old and new fields must match");
        }

        var model = GetModel(name);
        if (model == null)
        {
            return false;
        }

        var currentFields = GetModelFields(model);
        
        // Check that all old fields exist
        foreach (var oldField in oldFields)
        {
            if (!currentFields.Any(f => f.Name == oldField.Name && f.Type == oldField.Type))
            {
                throw new ArgumentException($"Field {oldField.Name}:{oldField.Type} not found in model");
            }
        }

        // Update fields
        for (int i = 0; i < oldFields.Count; i++)
        {
            var oldField = oldFields[i];
            var newField = newFields[i];
            var fieldIndex = currentFields.FindIndex(f => f.Name == oldField.Name && f.Type == oldField.Type);
            
            if (fieldIndex >= 0)
            {
                currentFields[fieldIndex] = newField;
            }
        }

        var fieldsJson = JsonSerializer.Serialize(currentFields);
        model.FieldsJson = fieldsJson;
        model.UpdatedAt = DateTime.UtcNow;

        if (model.ModelType == "temp")
        {
            _tempModels[name] = model;
        }
        else
        {
            var modelTable = _systemDatabaseService.GetModelTable();
            modelTable.Update(model);
        }

        return true;
    }

    /// <summary>
    /// View model
    /// </summary>
    public ModelDetails? SeeModel(string name)
    {
        var model = GetModel(name);
        if (model == null)
        {
            return null;
        }

        var fields = GetModelFields(model);
        return new ModelDetails
        {
            Name = model.Name,
            Type = model.ModelType,
            Fields = fields,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Delete model or fields from model
    /// </summary>
    public bool DeleteModel(string name, List<string>? fieldNames = null)
    {
        var model = GetModel(name);
        if (model == null)
        {
            return false;
        }

        // If field names specified, remove only them
        if (fieldNames != null && fieldNames.Count > 0)
        {
            var fields = GetModelFields(model);
            var fieldsToRemove = fieldNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            fields.RemoveAll(f => fieldsToRemove.Contains(f.Name));

            if (fields.Count == 0)
            {
                throw new InvalidOperationException("Cannot remove all fields from model. Delete the model entirely.");
            }

            var fieldsJson = JsonSerializer.Serialize(fields);
            model.FieldsJson = fieldsJson;
            model.UpdatedAt = DateTime.UtcNow;

            if (model.ModelType == "temp")
            {
                _tempModels[name] = model;
            }
            else
            {
                var modelTable = _systemDatabaseService.GetModelTable();
                modelTable.Update(model);
            }

            return true;
        }

        // Remove model entirely
        if (model.ModelType == "temp")
        {
            _tempModels.Remove(name);
        }
        else
        {
            var modelTable = _systemDatabaseService.GetModelTable();
            var allModels = modelTable.GetAll();
            var modelToDelete = allModels.FirstOrDefault(m => m.Name == name);
            if (modelToDelete != null)
            {
                modelTable.Delete(modelToDelete);
            }
        }

        return true;
    }

    /// <summary>
    /// Show model usage
    /// </summary>
    public ModelUsage? UseModel(string name)
    {
        var model = GetModel(name);
        if (model == null)
        {
            return null;
        }

        // In future can add model usage tracking
        // For now return basic info
        return new ModelUsage
        {
            ModelName = name,
            Databases = new List<string>(), // TODO: implement tracking
            Tables = new List<string>() // TODO: implement tracking
        };
    }

    /// <summary>
    /// Check if model exists
    /// </summary>
    public bool ModelExists(string name)
    {
        return GetModel(name) != null;
    }

    /// <summary>
    /// Get model
    /// </summary>
    private Model? GetModel(string name)
    {
        // First check temporary models
        if (_tempModels.TryGetValue(name, out var tempModel))
        {
            return tempModel;
        }

        // Then check permanent models
        var modelTable = _systemDatabaseService.GetModelTable();
        var allModels = modelTable.GetAll();
        return allModels.FirstOrDefault(m => m.Name == name);
    }

    /// <summary>
    /// Get model fields
    /// </summary>
    private List<ModelField> GetModelFields(Model model)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ModelField>>(model.FieldsJson) ?? new List<ModelField>();
        }
        catch
        {
            return new List<ModelField>();
        }
    }

    /// <summary>
    /// Create table from model
    /// </summary>
    public void CreateTableFromModel(Database database, string modelName, string tableName)
    {
        var model = GetModel(modelName);
        if (model == null)
        {
            throw new InvalidOperationException($"Model {modelName} not found");
        }

        var fields = GetModelFields(model);
        // TODO: Implement table creation from model
        // This will require reflection or dynamic type creation
    }
}

/// <summary>
/// Model info for list
/// </summary>
public class ModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Detailed model info
/// </summary>
public class ModelDetails
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<ModelField> Fields { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Model usage info
/// </summary>
public class ModelUsage
{
    public string ModelName { get; set; } = string.Empty;
    public List<string> Databases { get; set; } = new();
    public List<string> Tables { get; set; } = new();
}
