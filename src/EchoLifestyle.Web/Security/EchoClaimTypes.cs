namespace EchoLifestyle.Web.Security;

public static class EchoClaimTypes
{
    /// <summary>Staff or Customer. Checked by every back-office policy.</summary>
    public const string UserType = "echo:user_type";

    /// <summary>One claim per branch the user may act in.</summary>
    public const string Branch = "echo:branch";

    /// <summary>The branch selected on sign-in.</summary>
    public const string DefaultBranch = "echo:default_branch";

    public const string FullName = "echo:full_name";
}
