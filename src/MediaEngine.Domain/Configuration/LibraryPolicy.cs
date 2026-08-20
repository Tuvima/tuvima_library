namespace MediaEngine.Domain.Configuration;

public static class LibraryKinds
{
    public const string Catalogued = "catalogued";
    public const string Personal = "personal";
    public static bool IsValid(string? value) => value is Catalogued or Personal;
}

public static class LibraryAreas
{
    public const string Read = "read";
    public const string Watch = "watch";
    public const string Listen = "listen";
    public const string View = "view";
    public static bool IsValid(string? value) => value is Read or Watch or Listen or View;
}

public static class LibraryPresentations
{
    public const string Catalogue = "catalogue";
    public const string Gallery = "gallery";
    public const string MixedGallery = "mixed_gallery";
    public const string Timeline = "timeline";
    public const string Video = "video";
    public const string Documents = "documents";
    public const string Audio = "audio";
    public const string Mixed = "mixed";
    public static bool IsValid(string? value) => value is
        Catalogue or Gallery or MixedGallery or Timeline or Video or Documents or Audio or Mixed;
}

public static class LibraryMetadataPolicies
{
    public const string Enriched = "enriched";
    public const string LocalPreferred = "local_preferred";
    public const string LocalOnly = "local_only";
    public const string Manual = "manual";
    public static bool IsValid(string? value) => value is Enriched or LocalPreferred or LocalOnly or Manual;
    public static bool BypassesExternalIdentity(string? value) => value is LocalOnly or Manual;
}

public static class LibraryVisibility
{
    public const string Private = "private";
    public const string Shared = "shared";
    public const string Household = "household";
    public static bool IsValid(string? value) => value is Private or Shared or Household;
}

public static class LibraryIntakeModes
{
    public const string IncomingFolder = "incoming_folder";
    public const string DragAndDrop = "drag_and_drop";
    public const string BrowserUpload = "browser_upload";
    public const string MobileBackup = "mobile_backup";
    public const string ConnectedDeviceImport = "connected_device_import";
    public const string Api = "api";
    public static bool IsValid(string? value) => value is
        IncomingFolder or DragAndDrop or BrowserUpload or MobileBackup or ConnectedDeviceImport or Api;
}

public static class LibraryDuplicatePolicies
{
    public const string SkipExact = "skip_exact";
    public const string KeepBoth = "keep_both";
    public const string ReplaceExisting = "replace_existing";
    public static bool IsValid(string? value) => value is SkipExact or KeepBoth or ReplaceExisting;
}

public static class LibraryOrganizationModes
{
    public const string TuvimaStandard = "tuvima_standard";
    public const string KeepOriginalNames = "keep_original_names";
    public const string CaptureYearMonth = "capture_year_month";
    public const string YearMonthDay = "year_month_day";
    public const string KeepOriginalFolders = "keep_original_folders";
    public const string FlatFolder = "flat_folder";
    public const string Custom = "custom";
    public static bool IsValid(string? value) => value is
        TuvimaStandard or KeepOriginalNames or CaptureYearMonth or YearMonthDay
        or KeepOriginalFolders or FlatFolder or Custom;
}

public static class LibrarySourceRoles
{
    public const string PrimaryDestination = "primary_destination";
    public const string Secondary = "secondary";
    public const string Intake = "intake";
    public static bool IsValid(string? value) => value is PrimaryDestination or Secondary or Intake;
}

public static class LibrarySourceManagementModes
{
    public const string ManagedByTuvima = "managed_by_tuvima";
    public const string ExistingLibrary = "existing_library";
    public static bool IsValid(string? value) => value is ManagedByTuvima or ExistingLibrary;
}

public static class LibrarySourceTypes
{
    public const string LocalFolder = "local_folder";
    public const string NetworkShare = "network_share";
    public const string MobileDevice = "mobile_device";
    public const string ConnectedDevice = "connected_device";
    public static bool IsValid(string? value) => value is
        LocalFolder or NetworkShare or MobileDevice or ConnectedDevice;
}

public static class LibrarySourceAccessModes
{
    public const string Writable = "writable";
    public const string ReadOnly = "read_only";
    public static bool IsValid(string? value) => value is Writable or ReadOnly;
}

public static class LibrarySourceIntakeRoles
{
    public const string None = "none";
    public const string Direct = "direct";
    public const string MobileBackup = "mobile_backup";
    public const string DeviceImport = "device_import";
    public static bool IsValid(string? value) => value is None or Direct or MobileBackup or DeviceImport;
}

public static class IncomingSourcePurposes
{
    public const string SharedIntake = "shared_intake";
    public const string BrowserUploads = "browser_uploads";
    public const string DeviceIntake = "device_intake";
    public static bool IsValid(string? value) => value is SharedIntake or BrowserUploads or DeviceIntake;
}

public static class IncomingDefaultHandling
{
    public const string RouteAutomatically = "route_automatically";
    public const string StageThenRoute = "stage_then_route";
    public const string IndexInPlace = "index_in_place";
    public static bool IsValid(string? value) => value is RouteAutomatically or StageThenRoute or IndexInPlace;
}
