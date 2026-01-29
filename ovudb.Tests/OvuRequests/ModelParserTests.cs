using ovudb.OvuRequests;
using ovudb.OvuRequests.Ast;
using Xunit;

namespace ovudb.Tests.OvuRequests;

public class ModelParserTests
{
    [Fact]
    public void Parse_ModelAdd_ReturnsModelAddNode()
    {
        var parser = new Parser("MODEL ADD UserModel {id:Integer:key, name:String, age:Integer}");
        var result = parser.Parse();

        var modelAdd = Assert.IsType<ModelAddNode>(result);
        Assert.Equal("UserModel", modelAdd.ModelName);
        Assert.Equal(3, modelAdd.Fields.Count);
        Assert.True(modelAdd.Fields[0].IsKey);
        Assert.Equal("perm", modelAdd.ModelType); // Default
    }

    [Fact]
    public void Parse_ModelAddWithType_ReturnsModelAddNodeWithType()
    {
        var parser = new Parser("MODEL ADD TempModel {id:Integer} (temp)");
        var result = parser.Parse();

        var modelAdd = Assert.IsType<ModelAddNode>(result);
        Assert.Equal("TempModel", modelAdd.ModelName);
        Assert.Equal("temp", modelAdd.ModelType);
    }

    [Fact]
    public void Parse_ModelList_ReturnsModelListNode()
    {
        var parser = new Parser("MODEL LIST");
        var result = parser.Parse();

        Assert.IsType<ModelListNode>(result);
    }

    [Fact]
    public void Parse_ModelSee_ReturnsModelSeeNode()
    {
        var parser = new Parser("MODEL SEE UserModel");
        var result = parser.Parse();

        var modelSee = Assert.IsType<ModelSeeNode>(result);
        Assert.Equal("UserModel", modelSee.ModelName);
    }

    [Fact]
    public void Parse_ModelEdit_ReturnsModelEditNode()
    {
        var parser = new Parser("MODEL EDIT UserModel {name:String} {fullName:String}");
        var result = parser.Parse();

        var modelEdit = Assert.IsType<ModelEditNode>(result);
        Assert.Equal("UserModel", modelEdit.ModelName);
        Assert.Single(modelEdit.OldFields);
        Assert.Single(modelEdit.NewFields);
    }

    [Fact]
    public void Parse_ModelDel_ReturnsModelDelNode()
    {
        var parser = new Parser("MODEL DEL UserModel");
        var result = parser.Parse();

        var modelDel = Assert.IsType<ModelDelNode>(result);
        Assert.Equal("UserModel", modelDel.ModelName);
        Assert.Null(modelDel.FieldNames);
    }

    [Fact]
    public void Parse_ModelDelWithFields_ReturnsModelDelNodeWithFields()
    {
        var parser = new Parser("MODEL DEL UserModel {age, name}");
        var result = parser.Parse();

        var modelDel = Assert.IsType<ModelDelNode>(result);
        Assert.Equal("UserModel", modelDel.ModelName);
        Assert.NotNull(modelDel.FieldNames);
        Assert.Equal(2, modelDel.FieldNames.Count);
        Assert.Contains("age", modelDel.FieldNames);
        Assert.Contains("name", modelDel.FieldNames);
    }

    [Fact]
    public void Parse_ModelUse_ReturnsModelUseNode()
    {
        var parser = new Parser("MODEL USE UserModel");
        var result = parser.Parse();

        var modelUse = Assert.IsType<ModelUseNode>(result);
        Assert.Equal("UserModel", modelUse.ModelName);
    }

    [Fact]
    public void Parse_ModelAddWithMultipleFields_ReturnsModelAddNode()
    {
        var parser = new Parser("MODEL ADD ProductModel {id:Integer:key, name:String, price:Double, active:Boolean}");
        var result = parser.Parse();

        var modelAdd = Assert.IsType<ModelAddNode>(result);
        Assert.Equal(4, modelAdd.Fields.Count);
        Assert.True(modelAdd.Fields[0].IsKey);
        Assert.Equal("id", modelAdd.Fields[0].Name);
        Assert.Equal("Integer", modelAdd.Fields[0].Type);
    }

    [Fact]
    public void Parse_ModelAddWithoutKey_ReturnsModelAddNode()
    {
        var parser = new Parser("MODEL ADD SimpleModel {name:String, value:Integer}");
        var result = parser.Parse();

        var modelAdd = Assert.IsType<ModelAddNode>(result);
        Assert.Equal(2, modelAdd.Fields.Count);
        Assert.All(modelAdd.Fields, f => Assert.False(f.IsKey));
    }
}
