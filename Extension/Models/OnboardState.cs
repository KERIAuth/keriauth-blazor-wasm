﻿using System.Text.Json.Serialization;
using FluentResults;

namespace Extension.Models {
    public record OnboardState {
        [JsonPropertyName("HasAcknowledgedInstall")]
        public bool HasAcknowledgedInstall { get; init; }

        [JsonPropertyName("AcknowledgedInstalledVersion")]
        public string? AcknowledgedInstalledVersion { get; init; }

        [JsonPropertyName("TosAgreedUtc")]
        public DateTime? TosAgreedUtc { get; init; }

        [JsonPropertyName("TosAgreedHash")]
        public int TosAgreedHash { get; init; }

        [JsonPropertyName("PrivacyAgreedUtc")]
        public DateTime? PrivacyAgreedUtc { get; init; }

        [JsonPropertyName("PrivacyAgreedHash")]
        public int PrivacyAgreedHash { get; init; }

        public Result<bool> ValidateOnboardingStatus() {
            var errors = new List<IError>();
            
            if (!HasAcknowledgedInstall) {
                errors.Add(new ValidationError("Install", "Installation acknowledgment is required"));
            }
            
            if (AcknowledgedInstalledVersion is null) {
                errors.Add(new ValidationError("Version", "Acknowledged version is missing"));
            }
            
            if (TosAgreedUtc is null) {
                errors.Add(new ValidationError("Terms", "Terms of Service agreement is required"));
            }
            
            if (PrivacyAgreedUtc is null) {
                errors.Add(new ValidationError("Privacy", "Privacy Policy agreement is required"));
            }
            
            if (errors.Count > 0) {
                return Result.Fail<bool>(errors);
            }
            
            return Result.Ok(true);
        }
        
        // Keep deprecated method for backward compatibility
        [Obsolete("Use ValidateOnboardingStatus() instead")]
        public bool IsInstallOnboarded() {
            var result = ValidateOnboardingStatus();
            return result.IsSuccess;
        }
    }
}
