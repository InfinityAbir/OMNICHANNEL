using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Omnichannel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadAt",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalAccountId",
                table: "channel_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "channel_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_credentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_accounts_Type_ExternalAccountId",
                table: "channel_accounts",
                columns: new[] { "Type", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_credentials_ChannelAccountId",
                table: "channel_credentials",
                column: "ChannelAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_credentials_TenantId",
                table: "channel_credentials",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_credentials");

            migrationBuilder.DropIndex(
                name: "IX_channel_accounts_Type_ExternalAccountId",
                table: "channel_accounts");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ExternalAccountId",
                table: "channel_accounts");
        }
    }
}
