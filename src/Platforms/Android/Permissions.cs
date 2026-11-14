namespace SunsetMP;

public class AudioReadPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
    {
        get
        {
            var list = new List<(string androidPermission, bool isRuntime)>();
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                list.Add(("android.permission.READ_MEDIA_AUDIO", true));
            else
                list.Add(("android.permission.READ_EXTERNAL_STORAGE", true));
            return list.ToArray();
        }
    }
}

public class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions
        => new List<(string androidPermission, bool isRuntime)>
        {
            ("android.permission.POST_NOTIFICATIONS", true)
        }.ToArray();
}
