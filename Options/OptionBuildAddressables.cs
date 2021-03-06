//
// Trimmer Framework for Unity
// https://sttz.ch/trimmer
//

#if UNITY_EDITOR && TRIMMER_ADDRESSABLES

using System;
using UnityEngine;
using sttz.Trimmer.BaseOptions;
using UnityEditor.Build.Reporting;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets;
using UnityEditor.Build;
using UnityEditor;

namespace sttz.Trimmer.Options
{

/// <summary>
/// Option to build Addressables before a player build.
/// </summary>
/// <remarks>
/// The settings, profile id and data builder child options
/// can be left empty for the defaults to be used. Some of 
/// the options can be set to override the defaults, the 
/// option will try to revert any changes it has made.
/// </remarks>
[Capabilities(OptionCapabilities.ConfiguresBuild)]
public class OptionBuildAddressables : OptionToggle
{
    protected override void Configure()
    {
        Category = "Build";
    }

    /// <summary>
    /// Option to set the Addressable settings to use.
    /// </summary>
    /// <remarks>
    /// Defaults to the Addressables default settings object.
    /// </remarks>
    public class OptionSettings : OptionAsset<AddressableAssetSettings>
    {
        protected override void Configure()
        {
            DefaultValue = null;
        }
    }

    /// <summary>
    /// Option to select the Addressables profile (by id).
    /// </summary>
    /// <remarks>
    /// Defaults to the setting's active profile.
    /// </remarks>
    public class OptionProfileId : OptionString
    {
        protected override void Configure()
        {
            DefaultValue = "";
        }
    }

    /// <summary>
    /// Option to select the data builder script.
    /// </summary>
    /// <remarks>
    /// Defaults to the active player data builder. The builder object
    /// will be added to the settings object (the default if <see cref="OptionSettings"/>
    /// is not set) if it does not exist yet.
    /// </remarks>
    public class OptionDataBuilder : OptionAsset<ScriptableObject>
    {
        protected override void Configure()
        {
            DefaultValue = null;
        }
    }

    override public BuildPlayerOptions PrepareBuild(BuildPlayerOptions options, OptionInclusion inclusion)
    {
        options = base.PrepareBuild(options, inclusion);

        if (Value) BuildAddressables();

        return options;
    }

    void BuildAddressables()
    {
        // Original values to restore overrides
        var originalSettings = AddressableAssetSettingsDefaultObject.Settings;
        string originalProfileId = null;
        int originalDataBuilderIndex = -1;

        var settings = originalSettings;
        var result = default(AddressablesPlayerBuildResult);
        try {
            // Apply overrides
            var settingsOption = GetChild<OptionSettings>();
            if (settingsOption.Value != null) {
                settings = AddressableAssetSettingsDefaultObject.Settings = settingsOption.Value;
            }

            if (settings == null) {
                throw new BuildFailedException($"OptionBuildAddressables: No Addressables Asset Settings object set and no default set either.");
            }

            var profileOption = GetChild<OptionProfileId>();
            if (!string.IsNullOrEmpty(profileOption.Value)) {
                originalProfileId = settings.activeProfileId;
                settings.activeProfileId = profileOption.Value;
            }

            var builderOption = GetChild<OptionDataBuilder>();
            if (builderOption.Value != null) {
                var index = settings.DataBuilders.IndexOf(builderOption.Value);
                if (index < 0) {
                    if (!settings.AddDataBuilder((IDataBuilder)builderOption.Value)) {
                        throw new Exception($"OptionBuildAddressables: Failed to add data builder to settings (builder = {builderOption.Value})");
                    }
                    index = settings.DataBuilders.Count - 1;
                }
                originalDataBuilderIndex = settings.ActivePlayerDataBuilderIndex;
                settings.ActivePlayerDataBuilderIndex = index;
            }

            // Build!
            AddressableAssetSettings.BuildPlayerContent(out result);
        } finally {
            // Restore overrides
            if (settings != null) {
                if (originalDataBuilderIndex >= 0) {
                    settings.ActivePlayerDataBuilderIndex = originalDataBuilderIndex;
                }
                if (originalProfileId != null) {
                    settings.activeProfileId = originalProfileId;
                }
            }
            AddressableAssetSettingsDefaultObject.Settings = originalSettings;
        }

        if (!string.IsNullOrEmpty(result.Error)) {
            throw new BuildFailedException("OptionBuildAddressables: Addressables build failed with error:\n" + result.Error);
        }

        Debug.Log($"Built {result.LocationCount} Addressable assets in {result.Duration}s to {result.OutputPath}");
    }
}

}

#endif
