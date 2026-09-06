using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnsureControlPlaneSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "controlplane");

            migrationBuilder.RenameTable(
                name: "update_jobs",
                newName: "update_jobs",
                newSchema: "controlplane");

            migrationBuilder.RenameTable(
                name: "system_settings",
                newName: "system_settings",
                newSchema: "controlplane");

            migrationBuilder.RenameTable(
                name: "step_logs",
                newName: "step_logs",
                newSchema: "controlplane");

            migrationBuilder.RenameTable(
                name: "hosts",
                newName: "hosts",
                newSchema: "controlplane");

            migrationBuilder.RenameTable(
                name: "cluster_leases",
                newName: "cluster_leases",
                newSchema: "controlplane");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "update_jobs",
                schema: "controlplane",
                newName: "update_jobs");

            migrationBuilder.RenameTable(
                name: "system_settings",
                schema: "controlplane",
                newName: "system_settings");

            migrationBuilder.RenameTable(
                name: "step_logs",
                schema: "controlplane",
                newName: "step_logs");

            migrationBuilder.RenameTable(
                name: "hosts",
                schema: "controlplane",
                newName: "hosts");

            migrationBuilder.RenameTable(
                name: "cluster_leases",
                schema: "controlplane",
                newName: "cluster_leases");
        }
    }
}
