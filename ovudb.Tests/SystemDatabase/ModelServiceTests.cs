using ovudb.SystemDatabase;
using ovudb.SystemDatabase.Models;
using Xunit;

namespace ovudb.Tests.SystemDatabase;

public class ModelServiceTests : IDisposable
{
    private readonly string _testDataDirectory;
    private readonly SystemDatabaseService _systemDatabaseService;
    private readonly ModelService _modelService;

    public ModelServiceTests()
    {
        _testDataDirectory = Path.Combine(Path.GetTempPath(), $"ovudb_test_{Guid.NewGuid()}");
        _systemDatabaseService = new SystemDatabaseService(Path.Combine(_testDataDirectory, "ovusys"));
        _modelService = new ModelService(_systemDatabaseService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDirectory))
        {
            Directory.Delete(_testDataDirectory, true);
        }
    }

    [Fact]
    public void AddModel_PermModel_Success()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer", IsKey = true },
            new ModelField { Name = "name", Type = "String" },
            new ModelField { Name = "age", Type = "Integer" }
        };

        var result = _modelService.AddModel("UserModel", fields, "perm");
        Assert.True(result);
        Assert.True(_modelService.ModelExists("UserModel"));
    }

    [Fact]
    public void AddModel_TempModel_Success()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer", IsKey = true },
            new ModelField { Name = "name", Type = "String" }
        };

        var result = _modelService.AddModel("TempModel", fields, "temp");
        Assert.True(result);
        Assert.True(_modelService.ModelExists("TempModel"));
    }

    [Fact]
    public void AddModel_DuplicateName_ReturnsFalse()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer" }
        };

        _modelService.AddModel("TestModel", fields);
        var result = _modelService.AddModel("TestModel", fields);
        Assert.False(result);
    }

    [Fact]
    public void ListModels_ReturnsAllModels()
    {
        var fields1 = new List<ModelField> { new ModelField { Name = "id", Type = "Integer" } };
        var fields2 = new List<ModelField> { new ModelField { Name = "name", Type = "String" } };

        _modelService.AddModel("Model1", fields1, "perm");
        _modelService.AddModel("Model2", fields2, "temp");

        var models = _modelService.ListModels();
        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Name == "Model1" && m.Type == "perm");
        Assert.Contains(models, m => m.Name == "Model2" && m.Type == "temp");
    }

    [Fact]
    public void SeeModel_ExistingModel_ReturnsDetails()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer", IsKey = true },
            new ModelField { Name = "name", Type = "String" }
        };

        _modelService.AddModel("TestModel", fields);
        var details = _modelService.SeeModel("TestModel");

        Assert.NotNull(details);
        Assert.Equal("TestModel", details.Name);
        Assert.Equal(2, details.Fields.Count);
        Assert.True(details.Fields[0].IsKey);
    }

    [Fact]
    public void SeeModel_NonExistentModel_ReturnsNull()
    {
        var details = _modelService.SeeModel("NonExistent");
        Assert.Null(details);
    }

    [Fact]
    public void EditModel_ExistingModel_Success()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer" },
            new ModelField { Name = "name", Type = "String" }
        };

        _modelService.AddModel("TestModel", fields);

        var oldFields = new List<ModelField>
        {
            new ModelField { Name = "name", Type = "String" }
        };

        var newFields = new List<ModelField>
        {
            new ModelField { Name = "fullName", Type = "String" }
        };

        var result = _modelService.EditModel("TestModel", oldFields, newFields);
        Assert.True(result);

        var details = _modelService.SeeModel("TestModel");
        Assert.NotNull(details);
        Assert.Contains(details.Fields, f => f.Name == "fullName");
        Assert.DoesNotContain(details.Fields, f => f.Name == "name");
    }

    [Fact]
    public void DeleteModel_ExistingModel_Success()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer" }
        };

        _modelService.AddModel("TestModel", fields);
        var result = _modelService.DeleteModel("TestModel");
        
        Assert.True(result);
        Assert.False(_modelService.ModelExists("TestModel"));
    }

    [Fact]
    public void DeleteModel_DeleteFields_Success()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer" },
            new ModelField { Name = "name", Type = "String" },
            new ModelField { Name = "age", Type = "Integer" }
        };

        _modelService.AddModel("TestModel", fields);
        var result = _modelService.DeleteModel("TestModel", new List<string> { "age" });
        
        Assert.True(result);
        var details = _modelService.SeeModel("TestModel");
        Assert.NotNull(details);
        Assert.Equal(2, details.Fields.Count);
        Assert.DoesNotContain(details.Fields, f => f.Name == "age");
    }

    [Fact]
    public void UseModel_ExistingModel_ReturnsUsage()
    {
        var fields = new List<ModelField>
        {
            new ModelField { Name = "id", Type = "Integer" }
        };

        _modelService.AddModel("TestModel", fields);
        var usage = _modelService.UseModel("TestModel");

        Assert.NotNull(usage);
        Assert.Equal("TestModel", usage.ModelName);
    }

    [Fact]
    public void UseModel_NonExistentModel_ReturnsNull()
    {
        var usage = _modelService.UseModel("NonExistent");
        Assert.Null(usage);
    }
}
