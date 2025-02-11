// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq.Expressions;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;

namespace Aspire.Hosting;

/// <summary>
/// An Aspire resource that supports use of Azure Provisioning APIs to create Azure resources.
/// </summary>
/// <param name="name"></param>
/// <param name="configureConstruct"></param>
public class AzureConstructResource(string name, Action<ResourceModuleConstruct> configureConstruct) : AzureBicepResource(name, templateFile: $"{name}.module.bicep")
{
    /// <summary>
    /// Callback for configuring construct.
    /// </summary>
    public Action<ResourceModuleConstruct> ConfigureConstruct { get; } = configureConstruct;

    /// <inheritdoc/>
    public override BicepTemplateFile GetBicepTemplateFile(string? directory = null, bool deleteTemporaryFileOnDispose = true)
    {
        var configuration = new Configuration()
        {
            UseInteractiveMode = true
        };

        var resourceModuleConstruct = new ResourceModuleConstruct(this, configuration);

        foreach (var aspireParameter in this.Parameters)
        {
            var constructParameter = new Parameter(aspireParameter.Key);
            resourceModuleConstruct.AddParameter(constructParameter);
        }

        ConfigureConstruct(resourceModuleConstruct);

        var generationPath = Directory.CreateTempSubdirectory("aspire").FullName;
        resourceModuleConstruct.Build(generationPath);

        var moduleSourcePath = Path.Combine(generationPath, "main.bicep");
        var moduleDestinationPath = Path.Combine(directory ?? generationPath, $"{Name}.module.bicep");
        File.Copy(moduleSourcePath, moduleDestinationPath!, true);

        return new BicepTemplateFile(moduleDestinationPath, directory is null);
    }
}

/// <summary>
/// Extensions for working with <see cref="AzureConstructResource"/> and related types.
/// </summary>
public static class AzureConstructResourceExtensions
{
    /// <summary>
    /// Adds an Azure construct resource to the application model.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource being added.</param>
    /// <param name="configureConstruct">A callback used to configure the construct resource.</param>
    /// <returns></returns>
    public static IResourceBuilder<AzureConstructResource> AddAzureConstruct(this IDistributedApplicationBuilder builder, string name, Action<ResourceModuleConstruct> configureConstruct)
    {
        var resource = new AzureConstructResource(name, configureConstruct);
        return builder.AddResource(resource)
                      .WithManifestPublishingCallback(resource.WriteToManifest);
    }

    /// <summary>
    /// Assigns an Aspire parameter resource to an Azure construct resource.
    /// </summary>
    /// <typeparam name="T">Type of the CDK resource.</typeparam>
    /// <param name="resource">The CDK resource.</param>
    /// <param name="propertySelector">Property selection expression.</param>
    /// <param name="parameterResourceBuilder">Aspire parameter resource builder.</param>
    /// <param name="parameterName">The name of the parameter to be assigned.</param>
    public static void AssignParameter<T>(this Resource<T> resource, Expression<Func<T, object?>> propertySelector, IResourceBuilder<ParameterResource> parameterResourceBuilder, string? parameterName = null) where T: notnull
    {
        parameterName ??= parameterResourceBuilder.Resource.Name;

        if (resource.Scope is not ResourceModuleConstruct construct)
        {
            throw new ArgumentException("Cannot bind Aspire parameter resource to this construct.", nameof(resource));
        }

        construct.Resource.Parameters[parameterName] = parameterResourceBuilder;

        if (resource.Scope.GetParameters().Any(p => p.Name == parameterName))
        {
            var parameter = resource.Scope.GetParameters().Single(p => p.Name == parameterName);
            resource.AssignParameter(propertySelector, parameter);
        }
        else
        {
            var parameter = new Parameter(parameterName, isSecure: parameterResourceBuilder.Resource.Secret);
            resource.AssignParameter(propertySelector, parameter);
        }
    }

    /// <summary>
    /// Assigns an Aspire Bicep output reference to an Azure construct resource.
    /// </summary>
    /// <typeparam name="T">Type of the CDK resource.</typeparam>
    /// <param name="resource">The CDK resource.</param>
    /// <param name="propertySelector">Property selection expression.</param>
    /// <param name="parameterName">The name of the parameter to be assigned.</param>
    /// <param name="outputReference">Aspire parameter resource builder.</param>
    public static void AssignParameter<T>(this Resource<T> resource, Expression<Func<T, object?>> propertySelector, BicepOutputReference outputReference, string? parameterName = null) where T : notnull
    {
        parameterName ??= outputReference.Resource.Name;

        if (resource.Scope is not ResourceModuleConstruct construct)
        {
            throw new ArgumentException("Cannot bind Aspire parameter resource to this construct.", nameof(resource));
        }

        construct.Resource.Parameters[parameterName] = outputReference;

        if (resource.Scope.GetParameters().Any(p => p.Name == parameterName))
        {
            var parameter = resource.Scope.GetParameters().Single(p => p.Name == parameterName);
            resource.AssignParameter(propertySelector, parameter);
        }
        else
        {
            var parameter = new Parameter(parameterName);
            resource.AssignParameter(propertySelector, parameter);
        }
    }
}

/// <summary>
/// An Azure Provisioning construct which represents the root Bicep module that is generated for an Azure construct resource.
/// </summary>
public class ResourceModuleConstruct : Infrastructure
{
    internal ResourceModuleConstruct(AzureConstructResource resource, Configuration configuration) : base(constructScope: ConstructScope.ResourceGroup, tenantId: Guid.Empty, subscriptionId: Guid.Empty, envName: "temp", configuration: configuration)
    {
        Resource = resource;
    }

    /// <summary>
    /// The Azure cosntruct resource that this resource module construct represents.
    /// </summary>
    public AzureConstructResource Resource { get; }

    /// <summary>
    /// TODO:
    /// </summary>
    public Parameter PrincipalIdParameter => new Parameter("principalId");

    /// <summary>
    /// TODO:
    /// </summary>
    public Parameter PrincipalTypeParameter => new Parameter("principalType");
}
