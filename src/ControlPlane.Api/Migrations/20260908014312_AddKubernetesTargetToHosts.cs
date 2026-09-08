using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddKubernetesTargetToHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "k8s_cluster_id",
                schema: "controlplane",
                table: "hosts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "k8s_node_name",
                schema: "controlplane",
                table: "hosts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "k8s_cluster_id",
                schema: "controlplane",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "k8s_node_name",
                schema: "controlplane",
                table: "hosts");
        }
    }
}
