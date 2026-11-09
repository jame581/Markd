using Android.App;
using Android.Content;
using Markd.Services.Sync;
using Microsoft.Identity.Client;

namespace Markd.Platforms.Android;

/// <summary>Receives the Microsoft sign-in redirect (msal{client id}://auth) from the custom tab and hands it to MSAL.</summary>
[Activity(Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryBrowsable, Intent.CategoryDefault],
    DataHost = "auth",
    DataScheme = "msal" + CloudConfig.MicrosoftClientId)]
public class MsalActivity : BrowserTabActivity
{
}
