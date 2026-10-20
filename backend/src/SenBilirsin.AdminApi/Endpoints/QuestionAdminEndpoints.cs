using System.Security.Claims;
using SenBilirsin.Modules.Administration.Api;
using SenBilirsin.Modules.Administration.Application;
using SenBilirsin.Modules.Questions.Application;
using SenBilirsin.Modules.Questions.Domain;

namespace SenBilirsin.AdminApi.Endpoints;

public record QuestionRequest(int CategoryId, string? Text, string[]? Options, int CorrectIndex, Difficulty Difficulty,
    string? Explanation, QuestionStatus? Status, bool IgnoreDuplicates);

public record StatusRequest(QuestionStatus Status);
public record DuplicateCheckRequest(int CategoryId, string? Text, int? ExcludeId);
public record ImportRequest(string? Csv);
public record ImportCommitRequest(string? Csv, bool IncludeDuplicates, bool Publish);

/// <summary>Yönetici görünümü: doğru şık dahil (mobil API'de asla bulunmaz).</summary>
public record QuestionDto(int Id, int CategoryId, string Text, string[] Options, int CorrectIndex, Difficulty Difficulty,
    string? Explanation, QuestionStatus Status, int ShownCount, int CorrectCount, int ReportCount, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>Soru yönetimi uçları (docs/05 §2): ekle/düzenle, durum, liste/arama, tekrar kontrolü, CSV toplu yükleme.</summary>
public static class QuestionAdminEndpoints
{
    public static IEndpointRouteBuilder MapQuestionAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/questions").RequireAuthorization(AdminAuthEndpoints.PolicyName);

        group.MapGet("/", async (QuestionService questions, int? categoryId, Difficulty? difficulty, QuestionStatus? status,
            string? search, int? page, int? pageSize, CancellationToken ct) =>
        {
            var result = await questions.SearchAsync(new QuestionFilter(categoryId, difficulty, status, search, page ?? 1, pageSize ?? 25), ct);
            return Results.Ok(new { items = result.Items.Select(ToDto), total = result.Total, page = result.Page, pageSize = result.PageSize });
        });

        group.MapGet("/{id:int}", async (int id, QuestionService questions, CancellationToken ct) =>
            await questions.GetAsync(id, ct) is { } q ? Results.Ok(ToDto(q)) : NotFound());

        group.MapPost("/", async (QuestionRequest request, ClaimsPrincipal user, QuestionService questions, AuditLogger audit, CancellationToken ct) =>
        {
            var result = await questions.CreateAsync(ToInput(request), request.IgnoreDuplicates, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            var dto = ToDto(result.Value!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "question.create", "question", dto.Id.ToString(),
                after: CatalogAdminEndpoints.Json(dto), ct: ct);
            return Results.Created($"/api/questions/{dto.Id}", dto);
        });

        group.MapPut("/{id:int}", async (int id, QuestionRequest request, ClaimsPrincipal user, QuestionService questions, AuditLogger audit, CancellationToken ct) =>
        {
            var before = await questions.GetAsync(id, ct);
            var result = await questions.UpdateAsync(id, ToInput(request), request.IgnoreDuplicates, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            var dto = ToDto(result.Value!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "question.update", "question", id.ToString(),
                before: before is null ? null : CatalogAdminEndpoints.Json(ToDto(before)), after: CatalogAdminEndpoints.Json(dto), ct: ct);
            return Results.Ok(dto);
        });

        group.MapPost("/{id:int}/status", async (int id, StatusRequest request, ClaimsPrincipal user, QuestionService questions, AuditLogger audit, CancellationToken ct) =>
        {
            var before = await questions.GetAsync(id, ct);
            var result = await questions.SetStatusAsync(id, request.Status, ct);
            if (!result.IsSuccess) return Error(result.Error!);
            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "question.status", "question", id.ToString(),
                before: CatalogAdminEndpoints.Json(new { status = before?.Status }), after: CatalogAdminEndpoints.Json(new { status = request.Status }), ct: ct);
            return Results.Ok(ToDto(result.Value!));
        });

        // Yazarken anlık "benzer soru var" uyarısı için (forma kaydetmeden önce çağrılır)
        group.MapPost("/check-duplicate", async (DuplicateCheckRequest request, QuestionService questions, CancellationToken ct) =>
        {
            var text = request.Text ?? "";
            if (QuestionRules.Normalize(text).Length == 0) return Results.Ok(new { duplicates = Array.Empty<DuplicateMatch>() });
            var duplicates = await questions.FindDuplicatesAsync(QuestionRules.Normalize(text), request.CategoryId, request.ExcludeId, ct);
            return Results.Ok(new { duplicates });
        });

        group.MapPost("/import/preview", async (ImportRequest request, QuestionImportService import, CancellationToken ct) =>
        {
            var preview = await import.PreviewAsync(request.Csv, ct);
            return Results.Ok(new
            {
                fatalError = preview.FatalError,
                total = preview.Total, valid = preview.Valid, invalid = preview.Invalid, duplicates = preview.Duplicates,
                rows = preview.Rows.Select(r => new
                {
                    r.Line, r.Status, r.Errors, r.Duplicates, r.FileDuplicateOf, r.Mode, r.Category, r.Text,
                }),
            });
        });

        group.MapPost("/import/commit", async (ImportCommitRequest request, ClaimsPrincipal user, QuestionImportService import, AuditLogger audit, CancellationToken ct) =>
        {
            var result = await import.CommitAsync(request.Csv, request.IncludeDuplicates,
                request.Publish ? QuestionStatus.Published : QuestionStatus.Draft, ct);
            if (result.FatalError is not null)
                return Results.Json(new { code = "IMPORT_FAILED", message = result.FatalError }, statusCode: 400);

            await audit.WriteAsync(AdminAuthEndpoints.AdminIdOf(user), "question.import", "question", null,
                after: CatalogAdminEndpoints.Json(new { result.Created, result.SkippedInvalid, result.SkippedDuplicates, publish = request.Publish }), ct: ct);
            return Results.Ok(new { created = result.Created, skippedInvalid = result.SkippedInvalid, skippedDuplicates = result.SkippedDuplicates });
        });

        return app;
    }

    private static QuestionInput ToInput(QuestionRequest r) =>
        new(r.CategoryId, r.Text, r.Options, r.CorrectIndex, r.Difficulty, r.Explanation, r.Status ?? QuestionStatus.Draft);

    internal static QuestionDto ToDto(Question q) =>
        new(q.Id, q.CategoryId, q.Text, q.Options, q.CorrectIndex, q.Difficulty, q.Explanation, q.Status,
            q.ShownCount, q.CorrectCount, q.ReportCount, q.CreatedAt, q.UpdatedAt);

    private static IResult NotFound() => Results.Json(new { code = "QUESTION_NOT_FOUND", message = "Soru bulunamadı." }, statusCode: 404);

    private static IResult Error(QuestionError e) =>
        Results.Json(new { code = e.Code, message = e.Message, duplicates = e.Duplicates }, statusCode: e.StatusCode);
}
