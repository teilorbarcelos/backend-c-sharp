using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MageBackend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "Auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    request_password_token = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    request_password_expiration = table.Column<DateTime>(type: "datetime2", nullable: true),
                    retries = table.Column<int>(type: "int", nullable: false),
                    first_access = table.Column<bool>(type: "bit", nullable: false),
                    session_version = table.Column<int>(type: "int", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auth", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Feature",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feature", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tb_audit",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_user = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    action_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    execute_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    @class = table.Column<string>(name: "class", type: "nvarchar(max)", nullable: true),
                    function = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    @params = table.Column<string>(name: "params", type: "nvarchar(max)", nullable: true),
                    raw = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    table_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    diff_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    host = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    base_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hostname = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    original_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tb_error_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_user = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    source = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error_data = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tb_error_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RoleFeature",
                columns: table => new
                {
                    id_role = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_feature = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    create = table.Column<bool>(type: "bit", nullable: false),
                    view = table.Column<bool>(type: "bit", nullable: false),
                    activate = table.Column<bool>(type: "bit", nullable: false),
                    delete = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleFeature", x => new { x.id_role, x.id_feature });
                    table.ForeignKey(
                        name: "FK_RoleFeature_Feature_id_feature",
                        column: x => x.id_feature,
                        principalTable: "Feature",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleFeature_Role_id_role",
                        column: x => x.id_role,
                        principalTable: "Role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    cognito_id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    document = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    avatar = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    id_auth = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    id_role = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.id);
                    table.ForeignKey(
                        name: "FK_User_Auth_id_auth",
                        column: x => x.id_auth,
                        principalTable: "Auth",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_User_Role_id_role",
                        column: x => x.id_role,
                        principalTable: "Role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                columns: table => new
                {
                    id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    stock = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    id_user = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.id);
                    table.ForeignKey(
                        name: "FK_Product_User_id_user",
                        column: x => x.id_user,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Product_id_user",
                table: "Product",
                column: "id_user");

            migrationBuilder.CreateIndex(
                name: "IX_RoleFeature_id_feature",
                table: "RoleFeature",
                column: "id_feature");

            migrationBuilder.CreateIndex(
                name: "IX_User_id_auth",
                table: "User",
                column: "id_auth");

            migrationBuilder.CreateIndex(
                name: "IX_User_id_role",
                table: "User",
                column: "id_role");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Product");

            migrationBuilder.DropTable(
                name: "RoleFeature");

            migrationBuilder.DropTable(
                name: "tb_audit",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "tb_error_log",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "Feature");

            migrationBuilder.DropTable(
                name: "Auth");

            migrationBuilder.DropTable(
                name: "Role");
        }
    }
}
