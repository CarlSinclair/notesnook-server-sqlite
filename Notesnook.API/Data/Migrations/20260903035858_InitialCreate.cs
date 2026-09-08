using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notesnook.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    Platforms = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserTypes = table.Column<string>(type: "TEXT", nullable: false),
                    AppVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    UserIds = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CallToActions = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_pending_ids",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    Bucket = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_pending_ids", x => new { x.UserId, x.DeviceId, x.Bucket, x.ItemId, x.Type });
                });

            migrationBuilder.CreateTable(
                name: "inbox_api_keys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreated = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiryDate = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_api_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_sync_items",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Cipher = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    ItemId = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<double>(type: "REAL", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_sync_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "monographs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Slug = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    SelfDestruct = table.Column<bool>(type: "INTEGER", nullable: false),
                    EncryptedContent = table.Column<string>(type: "TEXT", nullable: true),
                    DatePublished = table.Column<long>(type: "INTEGER", nullable: false),
                    CompressedContent = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentSanitizationLevel = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monographs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sync_devices",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", nullable: false),
                    LastAccessTime = table.Column<long>(type: "INTEGER", nullable: false),
                    IsSyncReset = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", nullable: true),
                    DatabaseVersion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_devices", x => new { x.UserId, x.DeviceId });
                });

            migrationBuilder.CreateTable(
                name: "sync_items",
                columns: table => new
                {
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    DateSynced = table.Column<long>(type: "INTEGER", nullable: false),
                    IV = table.Column<string>(type: "TEXT", nullable: false),
                    Cipher = table.Column<string>(type: "TEXT", nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<double>(type: "REAL", nullable: false),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    Algorithm = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_items", x => new { x.UserId, x.Type, x.ItemId });
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LastSynced = table.Column<long>(type: "INTEGER", nullable: false),
                    Salt = table.Column<string>(type: "TEXT", nullable: false),
                    VaultKey = table.Column<string>(type: "TEXT", nullable: true),
                    AttachmentsKey = table.Column<string>(type: "TEXT", nullable: true),
                    MonographPasswordsKey = table.Column<string>(type: "TEXT", nullable: true),
                    DataEncryptionKey = table.Column<string>(type: "TEXT", nullable: true),
                    LegacyDataEncryptionKey = table.Column<string>(type: "TEXT", nullable: true),
                    InboxKeys = table.Column<string>(type: "TEXT", nullable: true),
                    StorageLimit = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_pending_ids_UserId_DeviceId_Bucket",
                table: "device_pending_ids",
                columns: new[] { "UserId", "DeviceId", "Bucket" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_api_keys_Key",
                table: "inbox_api_keys",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_api_keys_UserId",
                table: "inbox_api_keys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_sync_items_UserId_ItemId",
                table: "inbox_sync_items",
                columns: new[] { "UserId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_monographs_Slug",
                table: "monographs",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_monographs_UserId",
                table: "monographs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_sync_items_UserId",
                table: "sync_items",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_settings_UserId",
                table: "user_settings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "device_pending_ids");

            migrationBuilder.DropTable(
                name: "inbox_api_keys");

            migrationBuilder.DropTable(
                name: "inbox_sync_items");

            migrationBuilder.DropTable(
                name: "monographs");

            migrationBuilder.DropTable(
                name: "sync_devices");

            migrationBuilder.DropTable(
                name: "sync_items");

            migrationBuilder.DropTable(
                name: "user_settings");
        }
    }
}
