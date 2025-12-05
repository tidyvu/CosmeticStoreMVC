using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CosmeticStore.MVC.Migrations
{
    public partial class AddIsLockedToUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Carts_ProductVariants_VariantId",
                table: "Carts");

            migrationBuilder.RenameColumn(
                name: "VariantID",
                table: "Carts",
                newName: "VariantId");

            migrationBuilder.RenameColumn(
                name: "UserID",
                table: "Carts",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "ProductID",
                table: "Carts",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "CartID",
                table: "Carts",
                newName: "CartId");

            migrationBuilder.RenameIndex(
                name: "IX_Carts_VariantID",
                table: "Carts",
                newName: "IX_Carts_VariantId");

            migrationBuilder.RenameIndex(
                name: "IX_Carts_UserID",
                table: "Carts",
                newName: "IX_Carts_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Carts_ProductID",
                table: "Carts",
                newName: "IX_Carts_ProductId");

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OtpCode",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpExpiryTime",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedDate",
                table: "Carts",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "(getdate())");

            migrationBuilder.AddForeignKey(
                name: "FK_Carts_ProductVariants_VariantId",
                table: "Carts",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "VariantID",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Carts_ProductVariants_VariantId",
                table: "Carts");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OtpCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OtpExpiryTime",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "VariantId",
                table: "Carts",
                newName: "VariantID");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Carts",
                newName: "UserID");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "Carts",
                newName: "ProductID");

            migrationBuilder.RenameColumn(
                name: "CartId",
                table: "Carts",
                newName: "CartID");

            migrationBuilder.RenameIndex(
                name: "IX_Carts_VariantId",
                table: "Carts",
                newName: "IX_Carts_VariantID");

            migrationBuilder.RenameIndex(
                name: "IX_Carts_UserId",
                table: "Carts",
                newName: "IX_Carts_UserID");

            migrationBuilder.RenameIndex(
                name: "IX_Carts_ProductId",
                table: "Carts",
                newName: "IX_Carts_ProductID");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedDate",
                table: "Carts",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "(getdate())",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddForeignKey(
                name: "FK_Carts_ProductVariants_VariantId",
                table: "Carts",
                column: "VariantID",
                principalTable: "ProductVariants",
                principalColumn: "VariantID");
        }
    }
}
