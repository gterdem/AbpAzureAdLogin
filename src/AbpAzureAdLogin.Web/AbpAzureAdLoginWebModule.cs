﻿using System.IO;
using Localization.Resources.AbpUi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AbpAzureAdLogin.EntityFrameworkCore;
using AbpAzureAdLogin.Localization;
using AbpAzureAdLogin.MultiTenancy;
using AbpAzureAdLogin.Web.Menus;
using Microsoft.OpenApi.Models;
using Volo.Abp;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Authentication.JwtBearer;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Basic;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.AutoMapper;
using Volo.Abp.Identity.Web;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement.Web;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.UI.Navigation;
using Volo.Abp.VirtualFileSystem;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;

namespace AbpAzureAdLogin.Web
{
    [DependsOn(
        typeof(AbpAzureAdLoginHttpApiModule),
        typeof(AbpAzureAdLoginApplicationModule),
        typeof(AbpAzureAdLoginEntityFrameworkCoreDbMigrationsModule),
        typeof(AbpAutofacModule),
        typeof(AbpIdentityWebModule),
        typeof(AbpAccountWebIdentityServerModule),
        typeof(AbpAspNetCoreMvcUiBasicThemeModule),
        typeof(AbpAspNetCoreAuthenticationJwtBearerModule),
        typeof(AbpTenantManagementWebModule),
        typeof(AbpAspNetCoreSerilogModule)
        )]
    public class AbpAzureAdLoginWebModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
            {
                options.AddAssemblyResource(
                    typeof(AbpAzureAdLoginResource),
                    typeof(AbpAzureAdLoginDomainModule).Assembly,
                    typeof(AbpAzureAdLoginDomainSharedModule).Assembly,
                    typeof(AbpAzureAdLoginApplicationModule).Assembly,
                    typeof(AbpAzureAdLoginApplicationContractsModule).Assembly,
                    typeof(AbpAzureAdLoginWebModule).Assembly
                );
            });
        }

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            // Uncomment for debugging
            //context.Services
            //    .GetObject<IdentityBuilder>()
            //    .AddSignInManager<CustomSigninManager>();

            var hostingEnvironment = context.Services.GetHostingEnvironment();
            var configuration = context.Services.GetConfiguration();

            ConfigureUrls(configuration);
            ConfigureAuthentication(context, configuration);
            ConfigureAutoMapper();
            ConfigureVirtualFileSystem(hostingEnvironment);
            ConfigureLocalizationServices();
            ConfigureNavigationServices();
            ConfigureAutoApiControllers();
            ConfigureSwaggerServices(context.Services);
        }

        private void ConfigureUrls(IConfiguration configuration)
        {
            Configure<AppUrlOptions>(options =>
            {
                options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            });
        }

        private void ConfigureAuthentication(ServiceConfigurationContext context, IConfiguration configuration)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            // Mapping for GetExternalLoginInfoAsync
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Add("sub", ClaimTypes.NameIdentifier);
            context.Services.AddAuthentication()
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = configuration["AuthServer:Authority"];
                    options.RequireHttpsMetadata = false;
                    options.ApiName = "AbpAzureAdLogin";
                })
            //    .AddOpenIdConnect("AzureOpenId", "AzureAD", options =>
            //     {
            //         options.Authority = "https://login.microsoftonline.com/" + configuration["AzureAd:TenantId"];
            //         options.ClientId = configuration["AzureAd:ClientId"];
            //         options.ResponseType = OpenIdConnectResponseType.IdToken;
            //         options.CallbackPath = "/signin-oidc";
            //         options.RequireHttpsMetadata = false;
            //         options.SaveTokens = true;
            //         options.GetClaimsFromUserInfoEndpoint = true;

            //         options.Events.OnTokenValidated = (async context =>
            //         {
            //             var debugIdentityPrincipal = context.Principal.Identity;
            //             var claimsFromOidcProvider = context.Principal.Claims.ToList();
            //             await Task.CompletedTask;
            //         });
            //     });
            .AddAzureAD(options => configuration.Bind("AzureAd", options));
            // Same with commented above
            context.Services.Configure<OpenIdConnectOptions>(AzureADDefaults.OpenIdScheme, options =>
            {
                //options.Authority = options.Authority + "/v2.0/";         // Has problem with username
                options.Authority = "https://login.microsoftonline.com/" + configuration["AzureAd:TenantId"];
                options.ClientId = configuration["AzureAd:ClientId"];
                options.CallbackPath = configuration["AzureAd:CallbackPath"];

                options.ResponseType = OpenIdConnectResponseType.IdToken;
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters.ValidateIssuer = false; // accept several tenants (here simplified)                
                options.GetClaimsFromUserInfoEndpoint = true;
                options.SaveTokens = true;

                options.SignInScheme = IdentityConstants.ExternalScheme;
                
                options.Events.OnTokenValidated = (async context =>
                {
                    var debugIdentityPrincipal = context.Principal.Identity;
                    var claimsFromOidcProvider = context.Principal.Claims.ToList();
                    await Task.CompletedTask;
                });
            });
        }

        private void ConfigureAutoMapper()
        {
            Configure<AbpAutoMapperOptions>(options =>
            {
                options.AddMaps<AbpAzureAdLoginWebModule>();

            });
        }

        private void ConfigureVirtualFileSystem(IWebHostEnvironment hostingEnvironment)
        {
            if (hostingEnvironment.IsDevelopment())
            {
                Configure<AbpVirtualFileSystemOptions>(options =>
                {
                    options.FileSets.ReplaceEmbeddedByPhysical<AbpAzureAdLoginDomainSharedModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAzureAdLogin.Domain.Shared"));
                    options.FileSets.ReplaceEmbeddedByPhysical<AbpAzureAdLoginDomainModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAzureAdLogin.Domain"));
                    options.FileSets.ReplaceEmbeddedByPhysical<AbpAzureAdLoginApplicationContractsModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAzureAdLogin.Application.Contracts"));
                    options.FileSets.ReplaceEmbeddedByPhysical<AbpAzureAdLoginApplicationModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAzureAdLogin.Application"));
                    options.FileSets.ReplaceEmbeddedByPhysical<AbpAzureAdLoginWebModule>(hostingEnvironment.ContentRootPath);
                });
            }
        }

        private void ConfigureLocalizationServices()
        {
            Configure<AbpLocalizationOptions>(options =>
            {
                options.Resources
                    .Get<AbpAzureAdLoginResource>()
                    .AddBaseTypes(
                        typeof(AbpUiResource)
                    );

                options.Languages.Add(new LanguageInfo("cs", "cs", "Čeština"));
                options.Languages.Add(new LanguageInfo("en", "en", "English"));
                options.Languages.Add(new LanguageInfo("pt-BR", "pt-BR", "Português"));
                options.Languages.Add(new LanguageInfo("tr", "tr", "Türkçe"));
                options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "简体中文"));
                options.Languages.Add(new LanguageInfo("zh-Hant", "zh-Hant", "繁體中文"));
            });
        }

        private void ConfigureNavigationServices()
        {
            Configure<AbpNavigationOptions>(options =>
            {
                options.MenuContributors.Add(new AbpAzureAdLoginMenuContributor());
            });
        }

        private void ConfigureAutoApiControllers()
        {
            Configure<AbpAspNetCoreMvcOptions>(options =>
            {
                options.ConventionalControllers.Create(typeof(AbpAzureAdLoginApplicationModule).Assembly);
            });
        }

        private void ConfigureSwaggerServices(IServiceCollection services)
        {
            services.AddSwaggerGen(
                options =>
                {
                    options.SwaggerDoc("v1", new OpenApiInfo { Title = "AbpAzureAdLogin API", Version = "v1" });
                    options.DocInclusionPredicate((docName, description) => true);
                    options.CustomSchemaIds(type => type.FullName);
                }
            );
        }

        public override void OnApplicationInitialization(ApplicationInitializationContext context)
        {
            var app = context.GetApplicationBuilder();
            var env = context.GetEnvironment();

            app.UseCorrelationId();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseErrorPage();
            }
            app.UseVirtualFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseJwtTokenMiddleware();

            if (MultiTenancyConsts.IsEnabled)
            {
                app.UseMultiTenancy();
            }
            app.UseIdentityServer();
            app.UseAuthorization();
            app.UseAbpRequestLocalization();
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "AbpAzureAdLogin API");
            });
            app.UseAuditing();
            app.UseAbpSerilogEnrichers();
            app.UseMvcWithDefaultRouteAndArea();
        }

    }
}
