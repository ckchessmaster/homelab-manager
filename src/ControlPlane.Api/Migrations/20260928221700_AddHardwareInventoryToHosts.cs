using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHardwareInventoryToHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hardware_inventory",
                schema: "controlplane",
                table: "hosts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hardware_inventory",
                schema: "controlplane",
                table: "hosts");
        }
    }
}
