using System.Text;
using System.Text.Json;
using Chat.Application.Services.Classes.Groups;
using Chat.Application.Services.Classes.Messages;
using Chat.Application.Services.Classes.Organisation;
using Chat.Application.Services.Interfaces.Groups;
using Chat.Application.Services.Interfaces.Messages;
using Chat.Application.Services.Interfaces.Organisation;
using Chat.Infrastructure.Data;
using Chat.Infrastructure.Repositories.Classes.Groups;
using Chat.Infrastructure.Repositories.Classes.Messages;
using Chat.Infrastructure.Repositories.Classes.Organisation;
using Chat.Infrastructure.Repositories.Interfaces.Groups;
using Chat.Infrastructure.Repositories.Interfaces.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Organisation;
using Chat.Infrastructure.Services.Classes;
using Chat.Infrastructure.Services.Interfaces;
using Chat_Api.Hubs.Messages;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ─── Controllers ──────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// ─── SignalR (required by ChatController → IHubContext<ChatHub>) ───────────────
builder.Services.AddSignalR();

// ─── Swagger (+ JWT Authorize button) ─────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Chat API",
        Version = "v1"
    });

    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste SoftOnCloud JWT only (Swagger adds Bearer automatically)."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });
});

// ─── SoftOnCloud Product Connection (existing API consumer) ───────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(nameof(ProductConnectionService));
builder.Services.AddScoped<IProductConnectionService, ProductConnectionService>();

// ─── Infrastructure + Application (Chat) ──────────────────────────────────────
builder.Services.AddScoped<DatabaseHelper>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IOrganisationSettingRepository, OrganisationSettingRepository>();
builder.Services.AddScoped<IOrganisationSettingService, OrganisationSettingService>();
builder.Services.AddSingleton<Chat_Api.Helpers.ChatAttachmentStorage>();

// ─── SoftOnCloud JWT validation (same secret used to sign SoftOnCloud tokens) ─
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SoftOnCloud";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SoftOnCloud";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(2)
    };

    // SignalR browsers send JWT as ?access_token=
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// ─── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("ChatCors", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Avoid http→https redirect breaking Swagger "Failed to fetch" on localhost:5166
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseCors("ChatCors");
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (!path.StartsWith("/chat-attachments", StringComparison.OrdinalIgnoreCase))
            return;

        var origin = ctx.Context.Request.Headers.Origin.ToString();
        if (origin is "http://localhost:3000" or "http://localhost:5173")
        {
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
            ctx.Context.Response.Headers.Append("Vary", "Origin");
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapGet("/", () => "Chat API is running");

app.Run();
