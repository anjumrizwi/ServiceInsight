﻿namespace Particular.Licensing
{
    using System;
    using System.Collections.Generic;

    abstract class LicenseSource
    {
        protected string location;

        protected LicenseSource(string location)
        {
            this.location = location;
        }

        public abstract LicenseSourceResult Find(string applicationName);

        protected LicenseSourceResult ValidateLicense(string licenseText, string applicationName)
        {
            if (string.IsNullOrWhiteSpace(applicationName))
            {
                throw new ArgumentException("No application name specified");
            }

            var result = new LicenseSourceResult { Location = location };

            if (!LicenseVerifier.TryVerify(licenseText, out var failureMessage))
            {
                result.Result = $"License found in {location} is not valid - {failureMessage}";
                return result;
            }

            License license;
            try
            {
                license = LicenseDeserializer.Deserialize(licenseText);
            }
            catch
            {
                result.Result = $"License found in {location} could not be deserialized";
                return result;
            }

            if (license.ValidForApplication(applicationName))
            {
                result.License = license;
                result.Result = $"License found in {location}";
            }
            else
            {
                result.Result = $"License found in {location} was not valid for '{applicationName}'. Valid apps: '{string.Join(",", license.ValidApplications)}'";
            }
            return result;
        }

        public static List<LicenseSource> GetStandardLicenseSources()
        {
            var sources = new List<LicenseSource>();

            sources.Add(new LicenseSourceFilePath(LicenseFileLocationResolver.ApplicationFolderLicenseFile));
            sources.Add(new LicenseSourceFilePath(LicenseFileLocationResolver.GetPathFor(Environment.SpecialFolder.LocalApplicationData)));
            sources.Add(new LicenseSourceFilePath(LicenseFileLocationResolver.GetPathFor(Environment.SpecialFolder.CommonApplicationData)));

#if REGISTRYLICENSESOURCE
            sources.Add(new LicenseSourceHKCURegKey(@"SOFTWARE\ParticularSoftware"));
            sources.Add(new LicenseSourceHKLMRegKey(@"SOFTWARE\ParticularSoftware"));
#endif

            return sources;
        }
    }
}
