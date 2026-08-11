namespace SenorArroz.Application.Options;

public sealed class DeliveryAppVersionOptions
{
    public const string SectionName = "DeliveryAppVersion";
    public const string RequiredPackageName = "com.senorarroz.delivery_app";

    public bool Enabled { get; set; } = true;
    public string RequiredVersionName { get; set; } = "1.2.5";
    public int MinimumBuildNumber { get; set; } = 11;
    public string PlayStoreUrl { get; set; } =
        "https://play.google.com/store/apps/details?id=com.senorarroz.delivery_app";
}
