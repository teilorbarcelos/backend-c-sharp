import re
import os

with open("tests/IntegrationTests.cs", "r") as f:
    content = f.read()

replacements = {
    "Test_07_Health_Check_Endpoint": "GivenHealthEndpoint_WhenAccessed_ThenReturnsHealthy",
    "Test_07_Prometheus_Metrics_Endpoint": "GivenMetricsEndpoint_WhenAccessed_ThenReturnsPrometheusData",
    "Test_08_Rate_Limit_Headers": "GivenManyRequests_WhenLimitExceeded_ThenReturnsRateLimitHeaders",
    "Test_01_Login_Invalid_Credentials": "GivenInvalidCredentials_WhenLoggingIn_ThenReturnsUnauthorized",
    "Test_01_Login_Success": "GivenValidCredentials_WhenLoggingIn_ThenReturnsAuthTokensAndCreatesSession",
    "Test_01_Redis_Session_Created": "GivenValidLogin_WhenProcessed_ThenRedisSessionIsCreated",
    "Test_01_Refresh_Token": "GivenValidRefreshToken_WhenRefreshing_ThenReturnsNewTokens",
    "Test_01_Invalid_Tokens_Return_Unauthorized_Error": "GivenInvalidTokens_WhenAccessing_ThenReturnsUnauthorized",
    "Test_01_Get_Me_Structure": "GivenAuthenticatedUser_WhenRequestingMe_ThenReturnsValidStructure",
    "Test_01_Session_Invalidation_On_Mutation": "GivenMutationAction_WhenExecuted_ThenInvalidatesSession",
    "Test_01_Login_Inactive_User": "GivenInactiveUser_WhenLoggingIn_ThenReturnsForbidden",
    "Test_01_Login_Inactive_Role": "GivenInactiveRole_WhenLoggingIn_ThenReturnsForbidden",
    "Test_02_Rbac_Forbidden_Action": "GivenForbiddenAction_WhenExecuted_ThenReturnsForbidden",
    "Test_02_Rbac_Allowed_Action": "GivenAllowedAction_WhenExecuted_ThenProceedsSuccessfully",
    "Test_03_Schema_Missing_Required_Field": "GivenMissingRequiredField_WhenSubmitting_ThenReturnsBadRequest",
    "Test_03_Schema_Unknown_Field_Rejection": "GivenUnknownField_WhenSubmitting_ThenRejectsPayload",
    "Test_04_Dynamic_Filter_Success": "GivenDynamicFilter_WhenExecuting_ThenReturnsFilteredResults",
    "Test_04_Dynamic_Filter_Missing_Search_Fields": "GivenMissingSearchFields_WhenFiltering_ThenHandlesGracefully",
    "Test_04_Dynamic_Filter_Unmapped_Search_Field": "GivenUnmappedSearchField_WhenFiltering_ThenReturnsBadRequest",
    "Test_04_Dynamic_Filter_Unallowed_Filter_Key": "GivenUnallowedFilterKey_WhenFiltering_ThenReturnsBadRequest",
    "Test_04_Dynamic_Filter_Invalid_Date_Format": "GivenInvalidDateFormat_WhenFiltering_ThenReturnsBadRequest",
    "Test_04_Dynamic_Filter_Date_Range": "GivenDateRange_WhenFiltering_ThenReturnsCorrectResults",
    "Test_04_Dynamic_Filter_Active_Status": "GivenActiveStatus_WhenFiltering_ThenReturnsCorrectResults",
    "Test_04_Pagination_Size_Limit": "GivenPaginationLimits_WhenRequestingTooMany_ThenRestrictsSize",
    "Test_04_Listing_Sorting": "GivenSortingParameters_WhenListing_ThenReturnsSortedResults",
    "Test_05_Audit_Log_Created_On_Mutation": "GivenMutationAction_WhenExecuted_ThenCreatesAuditLog",
    "Test_05_Audit_Log_Ignores_Unauthenticated_Requests": "GivenUnauthenticatedRequest_WhenExecuted_ThenIgnoresAuditLog",
    "Test_05_Audit_Log_Password_Scrubbing": "GivenMutationAction_WhenLogging_ThenScrubsPasswordsFromLogs",
    "Test_06_Lgpd_User_Anonymization": "GivenLgpdRequest_WhenAnonymizing_ThenScramblesUserData",
    "Test_06_Soft_Delete_Behavior": "GivenSoftDeleteRequest_WhenDeleting_ThenMarksAsDeletedInsteadOfHardDelete",
    "Test_09_Toggle_Product_Status_By_Admin": "GivenAdminUser_WhenTogglingProductStatus_ThenStatusIsChanged",
    "Test_09_Toggle_Role_Status_By_Admin": "GivenAdminUser_WhenTogglingRoleStatus_ThenStatusIsChanged",
    "Test_09_Toggle_User_Status_By_Admin": "GivenAdminUser_WhenTogglingUserStatus_ThenStatusIsChanged",
    "Test_09_Toggle_Product_Status_Rbac_Forbidden": "GivenForbiddenUser_WhenTogglingProductStatus_ThenReturnsForbidden",
    "Test_09_Toggle_Product_Status_Rbac_Allowed": "GivenAllowedUser_WhenTogglingProductStatus_ThenStatusIsChanged",
    "Test_09_Toggle_Role_Status_Rbac_Forbidden": "GivenForbiddenUser_WhenTogglingRoleStatus_ThenReturnsForbidden",
    "Test_09_Toggle_Role_Status_Rbac_Allowed": "GivenAllowedUser_WhenTogglingRoleStatus_ThenStatusIsChanged",
    "Test_09_Toggle_User_Status_Rbac_Forbidden": "GivenForbiddenUser_WhenTogglingUserStatus_ThenReturnsForbidden",
    "Test_09_Toggle_User_Status_Rbac_Allowed": "GivenAllowedUser_WhenTogglingUserStatus_ThenStatusIsChanged",
    "Test_10_List_Role_Features_By_Admin": "GivenAdminRole_WhenListingFeatures_ThenReturnsAllFeatures",
    "Test_10_List_Role_Features_Rbac_Forbidden": "GivenForbiddenUser_WhenListingFeatures_ThenReturnsForbidden",
    "Test_10_List_Role_Features_Rbac_Allowed": "GivenAllowedUser_WhenListingFeatures_ThenReturnsAllFeatures",
    "Test_10_Get_Role_By_Id_Schema_Compliance": "GivenValidRoleId_WhenFetching_ThenReturnsCompliantSchema",
    "Test_11_Session_Invalidated_On_Role_Deactivation": "GivenRoleDeactivation_WhenExecuted_ThenInvalidatesUserSessions",
    "Test_11_Session_Invalidated_On_Role_Update": "GivenRoleUpdate_WhenExecuted_ThenInvalidatesUserSessions",
    "Test_11_Session_Invalidated_On_User_Deactivation": "GivenUserDeactivation_WhenExecuted_ThenInvalidatesSession",
    "Test_11_Session_Invalidated_On_User_Update": "GivenUserUpdate_WhenExecuted_ThenInvalidatesSession",
    "Test_12_Unhandled_Error_Logged_To_Db": "GivenUnhandledException_WhenOccurs_ThenLogsToDatabase",
    "Test_13_Pdf_Debug_Endpoints": "GivenPdfDebugEndpoint_WhenAccessed_ThenReturnsValidPdf",
}

for old_name, new_name in replacements.items():
    content = content.replace(old_name, new_name)

imports = re.match(r'(.*?namespace MageBackend\.Tests\s*\{)', content, re.DOTALL).group(1)

# we need to find sections correctly.
# a section is defined by a block starting with "// ==========================================" and ending before the next "// ==========================================\n        // --- "
# Let's extract all blocks by splitting on `\n        // ==========================================\n        // --- `
sections_raw = content.split('\n        // ==========================================\n        // --- ')

# sections_raw[0] contains imports and IntegrationTests base class
sections = {}
for sec in sections_raw[1:]:
    # sec starts with "07. Observability (2 tests) ----------\n        // ==========================================\n\n        [Fact]"
    lines = sec.split('\n')
    title_line = lines[0] # e.g. "07. Observability (2 tests) ----------"
    title_match = re.match(r'^(\d+\.\s+[\w\s\&]+)', title_line)
    if title_match:
        title = title_match.group(1).strip()
        sections[title] = "        // ==========================================\n        // --- " + sec

mapping = {
    "SystemTests": ["07. Observability", "08. Rate Limit", "12. Error Logs", "13. PDF Debug"],
    "AuthenticationTests": ["01. Auth & Session"],
    "RbacTests": ["02. RBAC Permissions", "10. Role Features"],
    "SchemaValidationTests": ["03. Schema Validation"],
    "QueryBuilderTests": ["04. Dynamic Filters"],
    "ComplianceTests": ["05. Audit Logs", "06. Soft Delete"],
    "UserManagementTests": ["09. Status", "11. Session Invalidation"]
}

for class_name, titles in mapping.items():
    class_body = ""
    for title in titles:
        if title in sections:
            # remove trailing "        // Helpers classes matching API structures" if it got into the last block
            body_part = sections[title]
            if "// Helpers classes matching API structures" in body_part:
                body_part = body_part[:body_part.find("// Helpers classes matching API structures")]
            class_body += body_part + "\n"

    file_content = f"{imports}\n    public class {class_name} : IntegrationTestBase\n    {{\n        public {class_name}(IntegrationTestFixture fixture) : base(fixture) {{ }}\n\n{class_body}    }}\n}}"
    with open(f"tests/Tests/{class_name}.cs", "w") as f:
        f.write(file_content)

print("Split completed successfully!")
