using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OnlineCoursePlatform.Configurations;
using OnlineCoursePlatform.Data.DbContext;
using OnlineCoursePlatform.Data.Entities;
using OnlineCoursePlatform.Hubs;
using OnlineCoursePlatform.Midlewares;
using OnlineCoursePlatform.Midlewares.Auth;
using OnlineCoursePlatform.Repositories.AuthRepositories;
using OnlineCoursePlatform.Repositories.AzureRepositories.BlobStorageRepositories;
using OnlineCoursePlatform.Repositories.AzureRepositories.MediaServiceRepositories;
using OnlineCoursePlatform.Repositories.CourseRepositories;
using OnlineCoursePlatform.Repositories.CourseTopicRepositories.Implementations;
using OnlineCoursePlatform.Repositories.CourseTopicRepositories.Interfaces;
using OnlineCoursePlatform.Repositories.CourseTypeRepositories;
using OnlineCoursePlatform.Repositories.UserRepositories;
using OnlineCoursePlatform.Services.AuthServices;
using OnlineCoursePlatform.Services.AuthServices.IAuthServices;
using OnlineCoursePlatform.Services.AzureBlobStorageServices;
using OnlineCoursePlatform.Services.AzureMediaServices;
using OnlineCoursePlatform.Services.BackgroundServices;
using OnlineCoursePlatform.Services.ChatServices;
using OnlineCoursePlatform.Services.CourseServices.Implementations;
using OnlineCoursePlatform.Services.CourseServices.Interfaces;
using OnlineCoursePlatform.Services.CourseTopicServices.Implementations;
using OnlineCoursePlatform.Services.CourseTopicServices.Interfaces;
using OnlineCoursePlatform.Services.CourseTypeServices.Implementations;
using OnlineCoursePlatform.Services.CourseTypeServices.Interfaces;
using OnlineCoursePlatform.Services.EmailServices;
using OnlineCoursePlatform.Services.LessonServices;
using OnlineCoursePlatform.Services.OrderServices;
using OnlineCoursePlatform.Services.PaymentServices;
using OnlineCoursePlatform.Services.UserServices.Implementations;
using OnlineCoursePlatform.Services.UserServices.Interfaces;
using Swashbuckle.AspNetCore.Filters;

var builder = WebApplication.CreateBuilder(args);
{
    // Add services to the container.
    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();

    // add dbcontext
    builder.Services
        .AddDbContextFactory<OnlineCoursePlatformDbContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
            //     ?? throw new InvalidOperationException("Connection String is not found"),
            // sqlOptions => sqlOptions.EnableRetryOnFailure());
        });

    // add identity
    builder.Services
        .AddIdentity<AppUser, IdentityRole>()
        .AddEntityFrameworkStores<OnlineCoursePlatformDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders()
        .AddRoles<IdentityRole>();

    builder.Services.Configure<VNPayConfig>(
        builder.Configuration.GetSection(VNPayConfig.ConfigName));


    // add jwt, google authentication
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    }).AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration[AppSettingsConfig.JWT_ISSUER],
            ValidAudience = builder.Configuration[AppSettingsConfig.JWT_AUDIENCE],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration[AppSettingsConfig.JWT_SECRETKEY]!))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var jwt = context.SecurityToken as JsonWebToken;
                if (jwt == null)
                {
                    context.Fail("Invalid token");
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = builder.Configuration[AppSettingsConfig.GOOGLE_CLIENTID_WEB]!;
        googleOptions.ClientSecret = builder.Configuration[AppSettingsConfig.GOOLE_CLIENTSECRET]!;
    });





    // Configuration swagger doc
    {

        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Description = "Please enter your token with this format: ''Bearer YOUR_TOKEN''",
                Type = SecuritySchemeType.ApiKey,
            });

            options.OperationFilter<SecurityRequirementsOperationFilter>();

            options.SwaggerDoc("v1", new OpenApiInfo()
            {
                Version = "v1",
                Title = "Online course platform service api",
                Description = "Sample .NET api by ",
                Contact = new OpenApiContact()
                {
                    Name = "Hoang Trong Tam",
                    Url = new Uri("https://www.youtube.com")
                }
            });

            options.EnableAnnotations();

            var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var path = Path.Combine(AppContext.BaseDirectory, xmlFileName);
            options.IncludeXmlComments(path);
        });
    }
}

// add sendmail service
builder.Services.AddTransient<IEmailSender, EmailSender>();

// add configure the behavior of API responses when the model state is invalid
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

// add helper
{
    // builder.Services.AddScoped<IAzureMediaService, AzureMediaService>();
    builder.Services.AddAzureClients(azureBuilder =>
    {
        azureBuilder.AddBlobServiceClient(builder.Configuration[AppSettingsConfig.AZURE_STORAGE_ACCOUNT_CONNECTIONSTRING]);
    });
}

// add repository service
{
    builder.Services.AddScoped<IAuthRepository, AuthRepository>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<ICourseTypeRepository, CourseTypeRepository>();
    builder.Services.AddScoped<ICourseTopicRepository, CourseTopicRepository>();
    builder.Services.AddScoped<ICourseRepository, CourseRepository>();
    builder.Services.AddScoped<IBlobStorageRepository, BlobStorageRepository>();
    builder.Services.AddScoped<IAzureMediaServiceRepository, AzureMediaServiceRepository>();
}

// add service
{
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<ILessonService, LessonService>();
    builder.Services.AddScoped<IChatService, ChatService>();
    builder.Services.AddScoped<IOrderService, OrderService>();
    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<ILoginService, LoginService>();
    builder.Services.AddScoped<IRegisterService, RegisterService>();
    builder.Services.AddScoped<IRoleService, RoleService>();
    builder.Services.AddScoped<IResetPasswordService, ResetPasswordService>();
    builder.Services.AddScoped<ILogOutService, LogOutService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ICourseTypeService, CourseTypeService>();
    builder.Services.AddScoped<ICourseTopicService, CourseTopicService>();
    builder.Services.AddScoped<ICourseService, CourseService>();
    builder.Services.AddScoped<IAzureMediaService, AzureMediaService>();
    builder.Services.AddScoped<IAzureBlobStorageService, AzureBlobStorageService>();
}

// Add SignalR Hub
{
    // builder.Services.AddSingleton<IHubContext<ProgressHub>>();
}

// Add background services
{
    builder.Services.AddHostedService<ResetViewAndSalesService>();
}

// add signalR
builder.Services.AddSignalR();
    //   .AddAzureSignalR(
    //       connectionString: builder.Configuration[AppSettingsConfig.AZURE_SIGNALR_CONNECTIONSTRING]);


var app = builder.Build();
{
    // Configure the HTTP request pipeline.
    //if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();

    // app.UseCors(options =>
    // {
    //     options
    //         .AllowAnyHeader()
    //         .AllowAnyOrigin()
    //         .AllowAnyMethod();
    // });

    app.UseCors(options =>
    {
        options
            .WithOrigins("http://localhost:5173", "https://online-course-react-app.azurewebsites.net") // Replace with the origin of your client app
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // This allows cookies, authorization headers etc.
    });


    app.MapControllers();

    // register middleware

    {
        app.UseMiddleware<JwtRevocationMiddleware>();
        // app.UseMiddleware<RateLimitMiddleware>(40, TimeSpan.FromSeconds(30));
    }


    // Map SignalR Hub
    {
        // app.MapHub<LessonHub>("lesson-hub");
        app.MapHub<ChatHub>("/chatHub");
        app.MapHub<ProgressHub>("/progressHub");
    }
    app.Run();
}