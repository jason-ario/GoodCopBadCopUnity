using UnityEngine;

namespace GoodCopBadCop.SuspectPaperwork
{
    public readonly struct SuspectPaperworkState
    {
        public readonly string FullName;
        public readonly string BirthDate;
        public readonly string Sex;
        public readonly string IdNumber;
        public readonly string ApplicationFullName;
        public readonly string ApplicationBirthDate;
        public readonly string ApplicationSex;
        public readonly string ApplicationIdNumber;
        public readonly string ApplicationExpirationDate;
        public readonly string EntryReason;
        public readonly string ExpirationDate;
        public readonly bool IsResident;
        public readonly bool ApplicationVisible;
        public readonly Texture IdPhoto;

        public SuspectPaperworkState(
            string fullName,
            string birthDate,
            string sex,
            string idNumber,
            string applicationFullName,
            string applicationBirthDate,
            string applicationSex,
            string applicationIdNumber,
            string applicationExpirationDate,
            string entryReason,
            string expirationDate,
            bool isResident,
            bool applicationVisible,
            Texture idPhoto)
        {
            FullName = fullName ?? string.Empty;
            BirthDate = birthDate ?? string.Empty;
            Sex = sex ?? string.Empty;
            IdNumber = idNumber ?? string.Empty;
            ApplicationFullName = applicationFullName ?? string.Empty;
            ApplicationBirthDate = applicationBirthDate ?? string.Empty;
            ApplicationSex = applicationSex ?? string.Empty;
            ApplicationIdNumber = applicationIdNumber ?? string.Empty;
            ApplicationExpirationDate = applicationExpirationDate ?? string.Empty;
            EntryReason = entryReason ?? string.Empty;
            ExpirationDate = expirationDate ?? string.Empty;
            IsResident = isResident;
            ApplicationVisible = applicationVisible;
            IdPhoto = idPhoto;
        }

        public static SuspectPaperworkState Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            true,
            null);
    }
}
