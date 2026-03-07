using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepareAtToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "prepare_at",
                table: "order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "prepared_notified_at",
                table: "order",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "prepare_at",
                table: "order");

            migrationBuilder.DropColumn(
                name: "prepared_notified_at",
                table: "order");
        }
    }
}
