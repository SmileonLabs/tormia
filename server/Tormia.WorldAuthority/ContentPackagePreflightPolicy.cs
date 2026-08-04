internal static class ContentPackagePreflightPolicy
{
    public static string? ResolveRejectionCode(
        bool activePackageMatch,
        int missingDefinitionCount,
        int checksumMismatchCount)
    {
        if (!activePackageMatch)
            return "content_package_not_active";
        if (missingDefinitionCount > 0)
            return "content_definition_missing";
        if (checksumMismatchCount > 0)
            return "content_definition_checksum_mismatch";
        return null;
    }
}
