using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Omnichannel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSecretsAiProviderEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_ai_provider_settings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_ai_provider_settings", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "tenant_email_settings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FromName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_email_settings", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "tenant_secrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EncryptedValue = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_secrets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_secrets_TenantId_Purpose",
                table: "tenant_secrets",
                columns: new[] { "TenantId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_ai_provider_settings");

            migrationBuilder.DropTable(
                name: "tenant_email_settings");

            migrationBuilder.DropTable(
                name: "tenant_secrets");
        }
    }
}
