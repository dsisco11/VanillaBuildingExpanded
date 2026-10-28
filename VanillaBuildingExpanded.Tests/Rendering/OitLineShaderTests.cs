using Moq;
using VanillaBuildingExpanded.Rendering;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaBuildingExpanded.Tests.Rendering;

public sealed class OitLineShaderTests
{
    [Fact]
    public void LeasesShareOneProgramUntilTheLastOwnerReleasesIt()
    {
        var api = new Mock<ICoreClientAPI>();
        var shaders = new Mock<IShaderAPI>();
        var program = new Mock<IShaderProgram>();
        program.Setup(p => p.Compile()).Returns(true);
        api.SetupGet(a => a.Shader).Returns(shaders.Object);
        shaders.Setup(s => s.NewShaderProgram()).Returns(program.Object);
        using var first = new OitLineShader(api.Object);
        using var second = new OitLineShader(api.Object);
        Assert.Same(first.GetProgram(), second.GetProgram());
        shaders.Verify(s => s.NewShaderProgram(), Times.Once);
        shaders.Verify(s => s.RegisterFileShaderProgram("vbe-oit-lines", program.Object), Times.Once);
        program.Verify(p => p.Compile(), Times.Once);
        first.Dispose();
        first.Dispose();
        program.Verify(p => p.Dispose(), Times.Never);
        Assert.Same(program.Object, second.GetProgram());
        Assert.Throws<ObjectDisposedException>(() => first.GetProgram());
        second.Dispose();
        second.Dispose();
        program.Verify(p => p.Dispose(), Times.Once);
        var replacement = new Mock<IShaderProgram>();
        replacement.Setup(p => p.Compile()).Returns(true);
        shaders.Setup(s => s.NewShaderProgram()).Returns(replacement.Object);
        using var next = new OitLineShader(api.Object);
        Assert.Same(replacement.Object, next.GetProgram());
        shaders.Verify(s => s.NewShaderProgram(), Times.Exactly(2));
    }

    [Fact]
    public void NativeReloadCreatesOneReplacementForAllOwners()
    {
        var api = new Mock<ICoreClientAPI>();
        var shaders = new Mock<IShaderAPI>();
        var original = new Mock<IShaderProgram>();
        var replacement = new Mock<IShaderProgram>();
        original.Setup(p => p.Compile()).Returns(true);
        replacement.Setup(p => p.Compile()).Returns(true);
        api.SetupGet(a => a.Shader).Returns(shaders.Object);
        shaders.SetupSequence(s => s.NewShaderProgram()).Returns(original.Object).Returns(replacement.Object);
        using var first = new OitLineShader(api.Object);
        using var second = new OitLineShader(api.Object);
        Assert.Same(original.Object, first.GetProgram());
        original.SetupGet(p => p.Disposed).Returns(true);
        Assert.Same(replacement.Object, second.GetProgram());
        Assert.Same(replacement.Object, first.GetProgram());
        shaders.Verify(s => s.NewShaderProgram(), Times.Exactly(2));
        replacement.Verify(p => p.Compile(), Times.Once);
        first.Dispose();
        replacement.Verify(p => p.Dispose(), Times.Never);
        second.Dispose();
        replacement.Verify(p => p.Dispose(), Times.Once);
        original.Verify(p => p.Dispose(), Times.Never);
    }

    [Theory]
    [InlineData("creation")]
    [InlineData("registration")]
    [InlineData("load")]
    [InlineData("compile")]
    public void FailedProgramsAreNeverExposedToAnotherOwnerAndAreCleanedUp(string failure)
    {
        var api = new Mock<ICoreClientAPI>();
        var shaders = new Mock<IShaderAPI>();
        var program = new Mock<IShaderProgram>();
        api.SetupGet(a => a.Shader).Returns(shaders.Object);
        shaders.Setup(s => s.NewShaderProgram())
            .Returns(failure == "creation" ? null! : program.Object);
        program.SetupGet(p => p.LoadError).Returns(failure == "load");
        program.Setup(p => p.Compile()).Returns(failure != "compile");
        if (failure == "registration")
            shaders.Setup(s => s.RegisterFileShaderProgram("vbe-oit-lines", program.Object))
                .Throws(new InvalidOperationException("GPU"));
        using var first = new OitLineShader(api.Object);
        using var second = new OitLineShader(api.Object);
        Assert.Throws<InvalidOperationException>(() => first.GetProgram());
        Assert.Throws<InvalidOperationException>(() => second.GetProgram());
        shaders.Verify(s => s.NewShaderProgram(), Times.Once);
        first.Dispose();
        program.Verify(p => p.Dispose(), Times.Never);
        second.Dispose();
        program.Verify(p => p.Dispose(), failure == "creation" ? Times.Never() : Times.Once());
    }
}
