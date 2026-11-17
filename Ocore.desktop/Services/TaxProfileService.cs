
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class TaxProfileService
    {
        private readonly string connectionString;

        public TaxProfileService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        private SQLiteConnection OpenConnection()
        {
            var connection = new SQLiteConnection(connectionString);
            connection.Open();

            using (var command = new SQLiteCommand(
                "PRAGMA foreign_keys = ON;", connection))
            {
                command.ExecuteNonQuery();
            }

            return connection;
        }

        private void CheckManager(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT Role
                  FROM Users
                  WHERE Id = @UserId
                    AND IsActive = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);

                object result = command.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو حسابه غير نشط.");
                }

                if (!string.Equals(
                    Convert.ToString(result),
                    "Manager",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        "إدارة التصنيفات الضريبية متاحة للمدير فقط.");
                }
            }
        }

        public List<TaxProfile> GetAllProfiles(
            bool includeInactive = false)
        {
            var profiles = new List<TaxProfile>();

            using (var connection = OpenConnection())
            {
                string sql = @"
                    SELECT
                        Id, Code, Name, TaxType, Description,
                        IsActive, IsSystem, CreatedBy,
                        CreatedAt, UpdatedAt
                    FROM TaxProfiles
                    WHERE (@IncludeInactive = 1 OR IsActive = 1)
                    ORDER BY IsSystem DESC, Name;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@IncludeInactive",
                        includeInactive ? 1 : 0);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            profiles.Add(ReadProfile(reader));
                        }
                    }
                }

                foreach (var profile in profiles)
                {
                    profile.Rates = GetRates(
                        connection,
                        null,
                        profile.Id);
                }
            }

            return profiles;
        }

        public TaxProfile GetProfileById(int profileId)
        {
            using (var connection = OpenConnection())
            {
                TaxProfile profile = null;

                using (var command = new SQLiteCommand(
                    @"SELECT
                        Id, Code, Name, TaxType, Description,
                        IsActive, IsSystem, CreatedBy,
                        CreatedAt, UpdatedAt
                      FROM TaxProfiles
                      WHERE Id = @Id
                      LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Id", profileId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            profile = ReadProfile(reader);
                        }
                    }
                }

                if (profile == null)
                    return null;

                profile.Rates = GetRates(
                    connection,
                    null,
                    profile.Id);

                return profile;
            }
        }

        private TaxProfile ReadProfile(SQLiteDataReader reader)
        {
            return new TaxProfile
            {
                Id = Convert.ToInt32(reader["Id"]),
                Code = Convert.ToString(reader["Code"]),
                Name = Convert.ToString(reader["Name"]),
                TaxType = Convert.ToString(reader["TaxType"]),
                Description = Convert.ToString(reader["Description"]),
                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                IsSystem = Convert.ToInt32(reader["IsSystem"]) == 1,

                CreatedBy = reader["CreatedBy"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(reader["CreatedBy"]),

                CreatedAt = ParseDate(reader["CreatedAt"]),
                UpdatedAt = ParseDate(reader["UpdatedAt"])
            };
        }

        private DateTime? ParseDate(object value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            DateTime result;

            if (DateTime.TryParse(
                Convert.ToString(value),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result))
            {
                return result;
            }

            return null;
        }

        private List<TaxProfileRate> GetRates(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int profileId)
        {
            var rates = new List<TaxProfileRate>();

            using (var command = new SQLiteCommand(
                @"SELECT
                    Id, TaxProfileId, ComponentCode,
                    ComponentName, ComponentType, RatePercent,
                    CalculationOrder, IsCompound,
                    EffectiveFrom, EffectiveTo,
                    IsActive, CreatedBy, CreatedAt
                  FROM TaxProfileRates
                  WHERE TaxProfileId = @ProfileId
                  ORDER BY EffectiveFrom, CalculationOrder, Id;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@ProfileId",
                    profileId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime effectiveFrom;

                        if (!DateTime.TryParse(
                            Convert.ToString(reader["EffectiveFrom"]),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out effectiveFrom))
                        {
                            effectiveFrom = DateTime.MinValue;
                        }

                        DateTime? effectiveTo = null;

                        if (reader["EffectiveTo"] != DBNull.Value &&
                            !string.IsNullOrWhiteSpace(
                                Convert.ToString(reader["EffectiveTo"])))
                        {
                            DateTime parsedDate;

                            if (DateTime.TryParse(
                                Convert.ToString(reader["EffectiveTo"]),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out parsedDate))
                            {
                                effectiveTo = parsedDate;
                            }
                        }

                        rates.Add(new TaxProfileRate
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            TaxProfileId = Convert.ToInt32(
                                reader["TaxProfileId"]),
                            ComponentCode = Convert.ToString(
                                reader["ComponentCode"]),
                            ComponentName = Convert.ToString(
                                reader["ComponentName"]),
                            ComponentType = Convert.ToString(
                                reader["ComponentType"]),
                            RatePercent = Convert.ToDouble(
                                reader["RatePercent"]),
                            CalculationOrder = Convert.ToInt32(
                                reader["CalculationOrder"]),
                            IsCompound = Convert.ToInt32(
                                reader["IsCompound"]) == 1,
                            EffectiveFrom = effectiveFrom,
                            EffectiveTo = effectiveTo,
                            IsActive = Convert.ToInt32(
                                reader["IsActive"]) == 1,
                            CreatedBy = reader["CreatedBy"] == DBNull.Value
                                ? (int?)null
                                : Convert.ToInt32(reader["CreatedBy"]),
                            CreatedAt = ParseDate(reader["CreatedAt"])
                        });
                    }
                }
            }

            return rates;
        }

        public int SaveProfile(
            TaxProfile profile,
            int currentUserId,
            string reason = "")
        {
            ValidateProfile(profile);

            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    CheckManager(
                        connection,
                        transaction,
                        currentUserId);

                    TaxProfile oldProfile = null;

                    if (profile.Id > 0)
                    {
                        oldProfile = GetProfileForUpdate(
                            connection,
                            transaction,
                            profile.Id);

                        if (oldProfile == null)
                        {
                            throw new InvalidOperationException(
                                "التصنيف الضريبي المطلوب غير موجود.");
                        }

                        if (oldProfile.IsSystem)
                        {
                            throw new InvalidOperationException(
                                "لا يمكن تعديل تصنيف ضريبي نظامي.");
                        }
                    }

                    ValidateRates(profile.Rates, profile.TaxType);

                    string now = DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture);

                    string code = profile.Code.Trim().ToUpperInvariant();
                    string name = profile.Name.Trim();
                    string description = profile.Description ?? "";

                    if (profile.Id == 0)
                    {
                        using (var command = new SQLiteCommand(
                            @"INSERT INTO TaxProfiles
                              (
                                  Code, Name, TaxType, Description,
                                  IsActive, IsSystem, CreatedBy,
                                  CreatedAt, UpdatedAt
                              )
                              VALUES
                              (
                                  @Code, @Name, @TaxType, @Description,
                                  @IsActive, 0, @CreatedBy,
                                  @CreatedAt, @UpdatedAt
                              );",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            command.Parameters.AddWithValue("@Name", name);
                            command.Parameters.AddWithValue(
                                "@TaxType", profile.TaxType);
                            command.Parameters.AddWithValue(
                                "@Description", description);
                            command.Parameters.AddWithValue(
                                "@IsActive", profile.IsActive ? 1 : 0);
                            command.Parameters.AddWithValue(
                                "@CreatedBy", currentUserId);
                            command.Parameters.AddWithValue(
                                "@CreatedAt", now);
                            command.Parameters.AddWithValue(
                                "@UpdatedAt", now);

                            command.ExecuteNonQuery();
                        }

                        profile.Id = Convert.ToInt32(
                            connection.LastInsertRowId);
                    }
                    else
                    {
                        using (var command = new SQLiteCommand(
                            @"UPDATE TaxProfiles
                              SET Code = @Code,
                                  Name = @Name,
                                  TaxType = @TaxType,
                                  Description = @Description,
                                  IsActive = @IsActive,
                                  UpdatedAt = @UpdatedAt
                              WHERE Id = @Id
                                AND IsSystem = 0;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            command.Parameters.AddWithValue("@Name", name);
                            command.Parameters.AddWithValue(
                                "@TaxType", profile.TaxType);
                            command.Parameters.AddWithValue(
                                "@Description", description);
                            command.Parameters.AddWithValue(
                                "@IsActive", profile.IsActive ? 1 : 0);
                            command.Parameters.AddWithValue(
                                "@UpdatedAt", now);
                            command.Parameters.AddWithValue(
                                "@Id", profile.Id);

                            if (command.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException(
                                    "تعذر تحديث التصنيف الضريبي.");
                            }
                        }

                        using (var command = new SQLiteCommand(
                            @"DELETE FROM TaxProfileRates
                              WHERE TaxProfileId = @ProfileId;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ProfileId",
                                profile.Id);

                            command.ExecuteNonQuery();
                        }
                    }

                    SaveRates(
                        connection,
                        transaction,
                        profile.Id,
                        profile.Rates,
                        currentUserId,
                        now);

                    string oldValues = oldProfile == null
                        ? ""
                        : BuildProfileSnapshot(oldProfile);

                    string newValues = BuildProfileSnapshot(profile);

                    WriteAudit(
                        connection,
                        transaction,
                        currentUserId,
                        oldProfile == null ? "Create" : "Update",
                        "TaxProfile",
                        profile.Id,
                        oldValues,
                        newValues,
                        reason,
                        now);

                    transaction.Commit();

                    return profile.Id;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private TaxProfile GetProfileForUpdate(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int profileId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT
                    Id, Code, Name, TaxType, Description,
                    IsActive, IsSystem, CreatedBy,
                    CreatedAt, UpdatedAt
                  FROM TaxProfiles
                  WHERE Id = @Id
                  LIMIT 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@Id", profileId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    var profile = ReadProfile(reader);

                    reader.Close();

                    profile.Rates = GetRates(
                        connection,
                        transaction,
                        profileId);

                    return profile;
                }
            }
        }

        private void ValidateProfile(TaxProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException("profile");

            if (string.IsNullOrWhiteSpace(profile.Code))
                throw new ArgumentException(
                    "يجب إدخال كود التصنيف الضريبي.");

            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new ArgumentException(
                    "يجب إدخال اسم التصنيف الضريبي.");

            if (profile.Code.Trim().Length > 50)
                throw new ArgumentException(
                    "كود التصنيف الضريبي طويل جدًا.");

            if (profile.Name.Trim().Length > 150)
                throw new ArgumentException(
                    "اسم التصنيف الضريبي طويل جدًا.");

            string[] allowedTypes =
            {
                "Exempt",
                "Standard",
                "Reduced",
                "Schedule"
            };

            if (!allowedTypes.Contains(profile.TaxType))
            {
                throw new ArgumentException(
                    "نوع المعاملة الضريبية غير صحيح.");
            }

            if (string.Equals(
                profile.Code.Trim(),
                "UNCLASSIFIED",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "هذا الكود محجوز للتصنيف النظامي غير المصنف.");
            }
        }

        private void ValidateRates(
            List<TaxProfileRate> rates,
            string taxType)
        {
            if (rates == null)
                rates = new List<TaxProfileRate>();

            if (taxType != "Exempt" && rates.Count == 0)
            {
                throw new ArgumentException(
                    "يجب إضافة مكون ضريبي واحد على الأقل لهذا التصنيف.");
            }

            foreach (var rate in rates)
            {
                if (rate == null)
                    throw new ArgumentException(
                        "يوجد مكون ضريبي غير صالح.");

                if (string.IsNullOrWhiteSpace(rate.ComponentCode))
                    throw new ArgumentException(
                        "يجب إدخال كود المكون الضريبي.");

                if (string.IsNullOrWhiteSpace(rate.ComponentName))
                    throw new ArgumentException(
                        "يجب إدخال اسم المكون الضريبي.");

                if (rate.RatePercent < 0 ||
                    double.IsNaN(rate.RatePercent) ||
                    double.IsInfinity(rate.RatePercent))
                {
                    throw new ArgumentException(
                        "نسبة الضريبة يجب أن تكون رقمًا صالحًا وغير سالب.");
                }

                if (rate.CalculationOrder < 1)
                    throw new ArgumentException(
                        "ترتيب الحساب يجب أن يبدأ من 1.");

                if (rate.ComponentType != "VAT" &&
                    rate.ComponentType != "Schedule")
                {
                    throw new ArgumentException(
                        "نوع المكون يجب أن يكون VAT أو Schedule.");
                }

                if (rate.EffectiveFrom == DateTime.MinValue)
                    throw new ArgumentException(
                        "يجب تحديد تاريخ بداية سريان النسبة.");

                if (rate.EffectiveTo.HasValue &&
                    rate.EffectiveTo.Value.Date <
                    rate.EffectiveFrom.Date)
                {
                    throw new ArgumentException(
                        "تاريخ نهاية السريان لا يمكن أن يسبق تاريخ البداية.");
                }
            }

            var orderedRates = rates
                .OrderBy(x => x.ComponentCode.Trim().ToUpperInvariant())
                .ThenBy(x => x.EffectiveFrom.Date)
                .ToList();

            for (int i = 1; i < orderedRates.Count; i++)
            {
                var previous = orderedRates[i - 1];
                var current = orderedRates[i];

                bool sameComponent = string.Equals(
                    previous.ComponentCode.Trim(),
                    current.ComponentCode.Trim(),
                    StringComparison.OrdinalIgnoreCase);

                if (!sameComponent)
                    continue;

                DateTime previousEnd = previous.EffectiveTo.HasValue
                    ? previous.EffectiveTo.Value.Date
                    : DateTime.MaxValue.Date;

                if (current.EffectiveFrom.Date <= previousEnd)
                {
                    throw new ArgumentException(
                        "توجد فترات سريان متداخلة لنفس المكون الضريبي.");
                }
            }
        }

        private void SaveRates(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int profileId,
            List<TaxProfileRate> rates,
            int currentUserId,
            string now)
        {
            if (rates == null)
                return;

            foreach (var rate in rates)
            {
                using (var command = new SQLiteCommand(
                    @"INSERT INTO TaxProfileRates
                      (
                          TaxProfileId,
                          ComponentCode,
                          ComponentName,
                          ComponentType,
                          RatePercent,
                          CalculationOrder,
                          IsCompound,
                          EffectiveFrom,
                          EffectiveTo,
                          IsActive,
                          CreatedBy,
                          CreatedAt
                      )
                      VALUES
                      (
                          @ProfileId,
                          @ComponentCode,
                          @ComponentName,
                          @ComponentType,
                          @RatePercent,
                          @CalculationOrder,
                          @IsCompound,
                          @EffectiveFrom,
                          @EffectiveTo,
                          @IsActive,
                          @CreatedBy,
                          @CreatedAt
                      );",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue(
                        "@ProfileId", profileId);
                    command.Parameters.AddWithValue(
                        "@ComponentCode", rate.ComponentCode.Trim());
                    command.Parameters.AddWithValue(
                        "@ComponentName", rate.ComponentName.Trim());
                    command.Parameters.AddWithValue(
                        "@ComponentType", rate.ComponentType);
                    command.Parameters.AddWithValue(
                        "@RatePercent", rate.RatePercent);
                    command.Parameters.AddWithValue(
                        "@CalculationOrder", rate.CalculationOrder);
                    command.Parameters.AddWithValue(
                        "@IsCompound", rate.IsCompound ? 1 : 0);
                    command.Parameters.AddWithValue(
                        "@EffectiveFrom",
                        rate.EffectiveFrom.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue(
                        "@EffectiveTo",
                        rate.EffectiveTo.HasValue
                            ? (object)rate.EffectiveTo.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture)
                            : "");
                    command.Parameters.AddWithValue(
                        "@IsActive", rate.IsActive ? 1 : 0);
                    command.Parameters.AddWithValue(
                        "@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue(
                        "@CreatedAt", now);

                    command.ExecuteNonQuery();
                }
            }
        }

        public void DeactivateProfile(
            int profileId,
            int currentUserId,
            string reason)
        {
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    CheckManager(
                        connection,
                        transaction,
                        currentUserId);

                    var profile = GetProfileForUpdate(
                        connection,
                        transaction,
                        profileId);

                    if (profile == null)
                        throw new InvalidOperationException(
                            "التصنيف الضريبي غير موجود.");

                    if (profile.IsSystem)
                        throw new InvalidOperationException(
                            "لا يمكن تعطيل التصنيف النظامي.");

                    if (!profile.IsActive)
                    {
                        transaction.Commit();
                        return;
                    }

                    using (var command = new SQLiteCommand(
                        @"UPDATE TaxProfiles
                          SET IsActive = 0,
                              UpdatedAt = @UpdatedAt
                          WHERE Id = @Id
                            AND IsSystem = 0;",
                        connection,
                        transaction))
                    {
                        command.Parameters.AddWithValue(
                            "@UpdatedAt",
                            DateTime.Now.ToString(
                                "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("@Id", profileId);

                        command.ExecuteNonQuery();
                    }

                    WriteAudit(
                        connection,
                        transaction,
                        currentUserId,
                        "Deactivate",
                        "TaxProfile",
                        profileId,
                        BuildProfileSnapshot(profile),
                        "IsActive=0",
                        reason,
                        DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture));

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private string BuildProfileSnapshot(TaxProfile profile)
        {
            if (profile == null)
                return "";

            string rates = "";

            if (profile.Rates != null)
            {
                rates = string.Join(
                    "; ",
                    profile.Rates.Select(rate =>
                        rate.ComponentCode + "=" +
                        rate.RatePercent.ToString(
                            CultureInfo.InvariantCulture) +
                        "% [" +
                        rate.EffectiveFrom.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture) +
                        " - " +
                        (rate.EffectiveTo.HasValue
                            ? rate.EffectiveTo.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture)
                            : "مفتوح") +
                        "]"));
            }

            return
                "Code=" + profile.Code +
                "|Name=" + profile.Name +
                "|TaxType=" + profile.TaxType +
                "|Description=" + profile.Description +
                "|IsActive=" + profile.IsActive +
                "|Rates=" + rates;
        }

        private void WriteAudit(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            string actionType,
            string entityType,
            int entityId,
            string oldValues,
            string newValues,
            string reason,
            string createdAt)
        {
            using (var command = new SQLiteCommand(
                @"INSERT INTO TaxAuditLogs
                  (
                      UserId,
                      ActionType,
                      EntityType,
                      EntityId,
                      OldValues,
                      NewValues,
                      Reason,
                      DeviceName,
                      CreatedAt
                  )
                  VALUES
                  (
                      @UserId,
                      @ActionType,
                      @EntityType,
                      @EntityId,
                      @OldValues,
                      @NewValues,
                      @Reason,
                      @DeviceName,
                      @CreatedAt
                  );",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@ActionType", actionType);
                command.Parameters.AddWithValue("@EntityType", entityType);
                command.Parameters.AddWithValue("@EntityId", entityId);
                command.Parameters.AddWithValue(
                    "@OldValues", oldValues ?? "");
                command.Parameters.AddWithValue(
                    "@NewValues", newValues ?? "");
                command.Parameters.AddWithValue(
                    "@Reason", reason ?? "");
                command.Parameters.AddWithValue(
                    "@DeviceName", Environment.MachineName);
                command.Parameters.AddWithValue(
                    "@CreatedAt", createdAt);

                command.ExecuteNonQuery();
            }
        }
    }
}
