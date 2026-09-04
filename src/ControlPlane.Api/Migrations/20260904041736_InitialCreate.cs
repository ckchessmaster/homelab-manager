using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ControlPlane.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cluster_leases",
                columns: table => new
                {
                    lease_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    holder_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cluster_leases", x => x.lease_key);
                });

            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    friendly_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    os_family = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    proxmox_node = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    proxmox_vmid = table.Column<int>(type: "integer", nullable: true),
                    idrac_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    unifi_switch_mac = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    unifi_switch_port = table.Column<int>(type: "integer", nullable: true),
                    agent_installed = table.Column<bool>(type: "boolean", nullable: false),
                    agent_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    agent_last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pending_reboot = table.Column<bool>(type: "boolean", nullable: false),
                    upgradable_packages_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hosts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "update_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    active_step = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    snapshot_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_update_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_update_jobs_hosts_target_host_id",
                        column: x => x.target_host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "step_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_id = table.Column<long>(type: "bigint", nullable: false),
                    stream_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    log_line = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_step_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_step_logs_update_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "update_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hosts_hostname",
                table: "hosts",
                column: "hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hosts_ip_address",
                table: "hosts",
                column: "ip_address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_step_logs_job_seq",
                table: "step_logs",
                columns: new[] { "job_id", "sequence_id" });

            migrationBuilder.CreateIndex(
                name: "ix_update_jobs_target_host_id",
                table: "update_jobs",
                column: "target_host_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cluster_leases");

            migrationBuilder.DropTable(
                name: "step_logs");

            migrationBuilder.DropTable(
                name: "update_jobs");

            migrationBuilder.DropTable(
                name: "hosts");
        }
    }
}
