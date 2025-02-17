using EasyMicroservices.CommentsMicroservice.Database;
using EasyMicroservices.CommentsMicroservice.Database.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using EasyMicroservices.CommentsMicroservice.Database.Entities;
using EasyMicroservices.CommentsMicroservice.Contracts;
using EasyMicroservices.CommentsMicroservice.Interfaces;
using EasyMicroservices.CommentsMicroservice.Database;
using EasyMicroservices.CommentsMicroservice.Interfaces;
using EasyMicroservices.CommentsMicroservice;
using EasyMicroservices.CommentsMicroservice.Contracts.Common;
using EasyMicroservices.CommentsMicroservice.Contracts.Requests;
using EasyMicroservices.Cores.Relational.EntityFrameworkCore;

namespace EasyMicroservices.CommentsMicroservice.WebApi
{
    public class Program
    {

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

            // Add services to the container.
            //builder.Services.AddAuthorization();
            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(options =>
            {
                options.SchemaFilter<GenericFilter>();
                options.SchemaFilter<XEnumNamesSchemaFilter>();
            });

            builder.Services.AddDbContext<CommentContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString(config.GetConnectionString("local")))
            );

            //builder.Services.AddScoped((serviceProvider) => new DependencyManager().GetContractLogic<FormEntity, CreateFormRequestContract, FormContract, FormContract>());
            string webRootPath = @Directory.GetCurrentDirectory();

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped((serviceProvider) => new DependencyManager().GetContractLogic<CommentEntity, AddCommentContract, UpdateCommentContract, CommentContract>());

            builder.Services.AddScoped<IDatabaseBuilder>(serviceProvider => new DatabaseBuilder(config));
            builder.Services.AddScoped(serviceProvider => new WhiteLabelManager(serviceProvider, serviceProvider.GetService<IDependencyManager>()));

            builder.Services.AddScoped<IDependencyManager>(service => new DependencyManager());
            builder.Services.AddTransient(serviceProvider => new CommentContext(serviceProvider.GetService<IDatabaseBuilder>()));
            builder.Services.AddTransient<RelationalCoreContext>(serviceProvider => serviceProvider.GetService<CommentContext>());


            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            // Configure the HTTP request pipeline.
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();


            //CreateDatabase();

            using (var scope = app.Services.CreateScope())
            {
                using var context = scope.ServiceProvider.GetService<CommentContext>();
                await context.Database.EnsureCreatedAsync();
                //await context.Database.MigrateAsync();
                await context.DisposeAsync();
                var service = scope.ServiceProvider.GetService<WhiteLabelManager>();
                await service.Initialize("Comment", config.GetValue<string>("RootAddresses:WhiteLabel"), typeof(CommentContext));
            }

            StartUp startUp = new StartUp();
            await startUp.Run(new DependencyManager());
            app.Run();
        }
        
        static void CreateDatabase()
        {
            IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

            using (var context = new CommentContext(new DatabaseBuilder(config)))
            {
                if (context.Database.EnsureCreated())
                {
                    //auto migration when database created first time

                    //add migration history table

                    string createEFMigrationsHistoryCommand = $@"
USE [{context.Database.GetDbConnection().Database}];
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
CREATE TABLE [dbo].[__EFMigrationsHistory](
    [MigrationId] [nvarchar](150) NOT NULL,
    [ProductVersion] [nvarchar](32) NOT NULL,
 CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY CLUSTERED 
(
    [MigrationId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY];
";
                    context.Database.ExecuteSqlRaw(createEFMigrationsHistoryCommand);

                    //insert all of migrations
                    var dbAssebmly = context.GetType().Assembly;
                    foreach (var item in dbAssebmly.GetTypes())
                    {
                        if (item.BaseType == typeof(Migration))
                        {
                            string migrationName = item.GetCustomAttributes<MigrationAttribute>().First().Id;
                            var version = typeof(Migration).Assembly.GetName().Version;
                            string efVersion = $"{version.Major}.{version.Minor}.{version.Build}";
                            context.Database.ExecuteSqlRaw("INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ({0},{1})", migrationName, efVersion);
                        }
                    }
                }
                context.Database.Migrate();
            }
        }
    }

    public class GenericFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            var type = context.Type;

            if (type.IsGenericType == false)
                return;

            schema.Title = $"{type.Name[0..^2]}<{type.GenericTypeArguments[0].Name}>";
        }
    }

    public class XEnumNamesSchemaFilter : ISchemaFilter
    {
        private const string NAME = "x-enumNames";
        public void Apply(OpenApiSchema model, SchemaFilterContext context)
        {
            var typeInfo = context.Type;
            // Chances are something in the pipeline might generate this automatically at some point in the future
            // therefore it's best to check if it exists.
            if (typeInfo.IsEnum && !model.Extensions.ContainsKey(NAME))
            {
                var names = Enum.GetNames(context.Type);
                var arr = new OpenApiArray();
                arr.AddRange(names.Select(name => new OpenApiString(name)));
                model.Extensions.Add(NAME, arr);
            }
        }
    }
}