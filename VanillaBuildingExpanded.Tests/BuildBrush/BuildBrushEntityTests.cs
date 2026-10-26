using Newtonsoft.Json.Linq;
using Moq;

using VanillaBuildingExpanded.BuildBrush;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaBuildingExpanded.Tests.BuildBrush;

public sealed class BuildBrushEntityTests
{
    [Fact]
    public void ClientPreview_RaisesOneNotificationPerObservedStateChangeWithoutRenderer()
    {
        var dimension = TestHelpers.CreateMockDimension();
        dimension.SetupGet(d => d.CurrentPos).Returns(new EntityPos());
        BlockPos origin = TestHelpers.CreateMiniDimensionPos(100, 100, 100);
        Block? block = null;
        dimension.Setup(d => d.GetBlock(It.Is<BlockPos>(pos => pos == origin))).Returns(() => block!);
        var entity = new BuildBrushEntity();
        entity.AssociateWithDimension(dimension.Object);
        entity.SetPreviewBounds(origin, origin);
        int notifications = 0;
        entity.PreviewChanged += () => notifications++;

        entity.RefreshClientPreview();
        entity.RefreshClientPreview();
        Assert.Equal(1, notifications);

        entity.IncrementBrushDirtyCounter();
        entity.RefreshClientPreview();
        Assert.Equal(2, notifications);

        block = TestHelpers.CreateTestBlock(100);
        entity.RefreshClientPreview();
        entity.RefreshClientPreview();
        Assert.Equal(3, notifications);

        block = TestHelpers.CreateTestBlock(101);
        entity.IncrementBrushDirtyCounter();
        entity.RefreshClientPreview();
        entity.RefreshClientPreview();
        Assert.Equal(4, notifications);

        block = null;
        entity.RefreshClientPreview();
        entity.RefreshClientPreview();
        Assert.Equal(5, notifications);
    }

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
