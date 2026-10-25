using Newtonsoft.Json.Linq;

using VanillaBuildingExpanded.BuildBrush;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BuildBrushEntityTests
{
    [Theory]
    [InlineData("client")]
    [InlineData("server")]
    public void PreviewAsset_HasNoPhysicsBehaviors(string side)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "vanillabuildingexpanded",
            "entities", "buildbrushentity.json");
        JObject asset = JObject.Parse(File.ReadAllText(path));

        Assert.Equal(BuildBrushEntity.ClassName, asset["class"]?.Value<string>());
        JArray behaviors = Assert.IsType<JArray>(asset[side]?["behaviors"]);
        Assert.Empty(behaviors);
    }

    [Fact]
    public void PreviewEntity_DisablesGravityAndInteraction()
    {
        BuildBrushEntity entity = new();

        Assert.False(entity.ApplyGravity);
        Assert.False(entity.IsInteractable);
    }
}
