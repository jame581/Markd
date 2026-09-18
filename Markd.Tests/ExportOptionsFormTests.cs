using Markd.Services;
using Markd.ViewModels;
using Xunit;

namespace Markd.Tests;

public class ExportOptionsFormTests
{
    [Fact]
    public void EncryptIsOnByDefault() => Assert.True(new ExportOptionsForm().Encrypt);

    [Fact]
    public void ShortPassword_IsRejected()
    {
        var form = new ExportOptionsForm { Password = "short", ConfirmPassword = "short" };

        Assert.Null(form.TryCreate(ExportDestination.Save));
        Assert.Equal("Use at least 8 characters.", form.ErrorMessage);
    }

    [Fact]
    public void Mismatch_IsRejected()
    {
        var form = new ExportOptionsForm { Password = "long enough", ConfirmPassword = "long enougH" };

        Assert.Null(form.TryCreate(ExportDestination.Save));
        Assert.Equal("The passwords do not match.", form.ErrorMessage);
    }

    [Fact]
    public void ValidPassword_ReturnsChoice()
    {
        var form = new ExportOptionsForm { Password = "long enough", ConfirmPassword = "long enough" };

        Assert.Equal(new ExportChoice("long enough", ExportDestination.Share), form.TryCreate(ExportDestination.Share));
        Assert.Null(form.ErrorMessage);
    }

    [Fact]
    public void Unencrypted_IgnoresPasswordFields()
    {
        var form = new ExportOptionsForm { Encrypt = false, Password = "x" };

        Assert.Equal(new ExportChoice(null, ExportDestination.Save), form.TryCreate(ExportDestination.Save));
    }
}
