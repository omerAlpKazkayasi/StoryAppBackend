using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Themes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Themes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoryUniverses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Tone = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MinimumAge = table.Column<int>(type: "int", nullable: false),
                    MaximumAge = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryUniverses", x => x.Id);
                    table.CheckConstraint("CK_StoryUniverses_AgeRange", "[MinimumAge] >= 0 AND [MaximumAge] >= [MinimumAge]");
                    table.ForeignKey(
                        name: "FK_StoryUniverses_Themes_ThemeId",
                        column: x => x.ThemeId,
                        principalTable: "Themes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryUniverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Atmosphere = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VisualStyle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NaturalElements = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MinimumAge = table.Column<int>(type: "int", nullable: false),
                    MaximumAge = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regions_StoryUniverses_StoryUniverseId",
                        column: x => x.StoryUniverseId,
                        principalTable: "StoryUniverses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoryObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryUniverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    VisualDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StoryFunction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsMagical = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryObjects_StoryUniverses_StoryUniverseId",
                        column: x => x.StoryUniverseId,
                        principalTable: "StoryUniverses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryUniverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Personality = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Motivation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SpeakingStyle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VisualDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SpecialAbility = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MinimumAge = table.Column<int>(type: "int", nullable: false),
                    MaximumAge = table.Column<int>(type: "int", nullable: false),
                    CanBeMainCharacter = table.Column<bool>(type: "bit", nullable: false),
                    CanBeSupportingCharacter = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Characters_StoryUniverses_StoryUniverseId",
                        column: x => x.StoryUniverseId,
                        principalTable: "StoryUniverses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EducationFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryUniverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Topic = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Fact = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsageHint = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MinimumAge = table.Column<int>(type: "int", nullable: false),
                    MaximumAge = table.Column<int>(type: "int", nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EducationFacts_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EducationFacts_StoryUniverses_StoryUniverseId",
                        column: x => x.StoryUniverseId,
                        principalTable: "StoryUniverses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Stories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UniverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChildAge = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RootNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stories_StoryUniverses_UniverseId",
                        column: x => x.UniverseId,
                        principalTable: "StoryUniverses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoryNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IsEnding = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryNodes", x => x.Id);
                    table.CheckConstraint("CK_StoryNodes_PartNumber", "[PartNumber] > 0");
                    table.ForeignKey(
                        name: "FK_StoryNodes_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryNodeMemories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MainCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentRegionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentGoal = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompanionCharacterIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActiveObjectIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpenThreadsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportantEventsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EstablishedFactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NarrativeAngleUsed = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryNodeMemories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryNodeMemories_StoryNodes_StoryNodeId",
                        column: x => x.StoryNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryNodeScenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageObjectKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryNodeScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryNodeScenes_StoryNodes_StoryNodeId",
                        column: x => x.StoryNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoryReadingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentStep = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryReadingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryReadingSessions_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoryReadingSessions_StoryNodes_CurrentNodeId",
                        column: x => x.CurrentNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoryTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChoiceTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ChoiceIntent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NextTargetMomentum = table.Column<int>(type: "int", nullable: false),
                    MeetNewCharacter = table.Column<bool>(type: "bit", nullable: false),
                    ChangeRegion = table.Column<bool>(type: "bit", nullable: false),
                    IntroduceNewObject = table.Column<bool>(type: "bit", nullable: false),
                    IncludeEducation = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryTransitions", x => x.Id);
                    table.CheckConstraint("CK_StoryTransitions_DifferentNodes", "[ToNodeId] IS NULL OR [FromNodeId] <> [ToNodeId]");
                    table.ForeignKey(
                        name: "FK_StoryTransitions_StoryNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoryTransitions_StoryNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoryReadingHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadingSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryReadingHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryReadingHistories_StoryNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoryReadingHistories_StoryNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "StoryNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StoryReadingHistories_StoryReadingSessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "StoryReadingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoryReadingHistories_StoryTransitions_TransitionId",
                        column: x => x.TransitionId,
                        principalTable: "StoryTransitions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_RegionId",
                table: "Characters",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_StoryUniverseId",
                table: "Characters",
                column: "StoryUniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_StoryUniverseId_Code",
                table: "Characters",
                columns: new[] { "StoryUniverseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationFacts_RegionId",
                table: "EducationFacts",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_EducationFacts_StoryUniverseId",
                table: "EducationFacts",
                column: "StoryUniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_StoryUniverseId",
                table: "Regions",
                column: "StoryUniverseId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_StoryUniverseId_Code",
                table: "Regions",
                columns: new[] { "StoryUniverseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stories_RootNodeId",
                table: "Stories",
                column: "RootNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stories_UniverseId_Status",
                table: "Stories",
                columns: new[] { "UniverseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StoryNodeMemories_StoryNodeId",
                table: "StoryNodeMemories",
                column: "StoryNodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryNodes_StoryId_PartNumber",
                table: "StoryNodes",
                columns: new[] { "StoryId", "PartNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryNodeScenes_StoryNodeId_SortOrder",
                table: "StoryNodeScenes",
                columns: new[] { "StoryNodeId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryObjects_StoryUniverseId_Code",
                table: "StoryObjects",
                columns: new[] { "StoryUniverseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingHistories_FromNodeId",
                table: "StoryReadingHistories",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingHistories_ReadingSessionId_StepNumber",
                table: "StoryReadingHistories",
                columns: new[] { "ReadingSessionId", "StepNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingHistories_ToNodeId",
                table: "StoryReadingHistories",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingHistories_TransitionId",
                table: "StoryReadingHistories",
                column: "TransitionId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingSessions_CurrentNodeId",
                table: "StoryReadingSessions",
                column: "CurrentNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingSessions_StoryId",
                table: "StoryReadingSessions",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingSessions_UserId_Status",
                table: "StoryReadingSessions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StoryReadingSessions_UserId_StoryId",
                table: "StoryReadingSessions",
                columns: new[] { "UserId", "StoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoryTransitions_FromNodeId_SortOrder",
                table: "StoryTransitions",
                columns: new[] { "FromNodeId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryTransitions_ToNodeId",
                table: "StoryTransitions",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryUniverses_ThemeId_IsActive",
                table: "StoryUniverses",
                columns: new[] { "ThemeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Themes_IsActive_SortOrder",
                table: "Themes",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_StoryNodes_RootNodeId",
                table: "Stories",
                column: "RootNodeId",
                principalTable: "StoryNodes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stories_StoryUniverses_UniverseId",
                table: "Stories");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_StoryNodes_RootNodeId",
                table: "Stories");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "EducationFacts");

            migrationBuilder.DropTable(
                name: "StoryNodeMemories");

            migrationBuilder.DropTable(
                name: "StoryNodeScenes");

            migrationBuilder.DropTable(
                name: "StoryObjects");

            migrationBuilder.DropTable(
                name: "StoryReadingHistories");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "StoryReadingSessions");

            migrationBuilder.DropTable(
                name: "StoryTransitions");

            migrationBuilder.DropTable(
                name: "StoryUniverses");

            migrationBuilder.DropTable(
                name: "Themes");

            migrationBuilder.DropTable(
                name: "StoryNodes");

            migrationBuilder.DropTable(
                name: "Stories");
        }
    }
}
