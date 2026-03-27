using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations;

/// <summary>
/// Solo añade coordenadas a branch (bases ya existentes). No recrea el esquema completo.
/// </summary>
public partial class AddBranchLatitudeLongitude : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        migrationBuilder.AddColumn<decimal>(
            name: "latitude",
            table: "branch",
            type: "numeric(10,6)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "longitude",
            table: "branch",
            type: "numeric(10,6)",
            nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        migrationBuilder.DropColumn(
            name: "latitude",
            table: "branch");

        migrationBuilder.DropColumn(
            name: "longitude",
            table: "branch");
    }
}
