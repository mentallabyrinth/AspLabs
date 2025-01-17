// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DocFx.XmlComments;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.AspNetCore.OpenApi.SourceGenerators;

public sealed partial class XmlCommentGenerator
{
    internal static IEnumerable<(string, string?, XmlComment?)> ParseComments(Compilation compilation, CancellationToken cancellationToken)
    {
        var visitor = new AssemblyTypeSymbolsVisitor(cancellationToken);
        visitor.VisitAssembly(compilation.Assembly);
        var types = visitor.GetPublicTypes();
        var comments = new List<(string, string?, XmlComment?)>();
        foreach (var type in types)
        {
            var comment = type.GetDocumentationComment(
                compilation: compilation,
                preferredCulture: CultureInfo.InvariantCulture,
                expandIncludes: true,
                expandInheritdoc: true,
                cancellationToken: cancellationToken);
            if (!string.IsNullOrEmpty(comment) && !string.Equals("<doc />", comment, StringComparison.Ordinal))
            {
                var typeInfo = type.ToNormalizedDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var typeComment = XmlComment.Parse(comment, new());
                comments.Add((typeInfo, null, typeComment));
            }
        }
        var properties = visitor.GetPublicProperties();
        foreach (var property in properties)
        {
            var comment = property.GetDocumentationComment(
                compilation: compilation,
                preferredCulture: CultureInfo.InvariantCulture,
                expandIncludes: true,
                expandInheritdoc: true,
                cancellationToken: cancellationToken);
            if (!string.IsNullOrEmpty(comment) && !string.Equals("<doc />", comment, StringComparison.Ordinal))
            {
                var typeInfo = property.ContainingType.ToNormalizedDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var propertyInfo = property.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var propertyComment = XmlComment.Parse(comment, new());
                if (propertyComment is not null)
                {
                    propertyComment.Type = property.Type;
                    comments.Add((typeInfo, propertyInfo, propertyComment));
                }
            }
        }
        var methods = visitor.GetPublicMethods();
        foreach (var method in methods)
        {
            var comment = method.GetDocumentationComment(
                compilation: compilation,
                preferredCulture: CultureInfo.InvariantCulture,
                expandIncludes: true,
                expandInheritdoc: true,
                cancellationToken: cancellationToken);
            if (!string.IsNullOrEmpty(comment) && !string.Equals("<doc />", comment, StringComparison.Ordinal))
            {
                // If the method is a constructor for a record, skip it because we will have already processed the type.
                if (method.MethodKind == MethodKind.Constructor)
                {
                    continue;
                }
                var typeInfo = method.ContainingType.ToNormalizedDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var methodInfo = method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                comments.Add((typeInfo, methodInfo, XmlComment.Parse(comment, new())));
            }
        }
        return comments;
    }

    internal static bool FilterInvocations(SyntaxNode node, CancellationToken _)
        => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddOpenApi" } };

    internal static AddOpenApiInvocation GetAddOpenApiOverloadVariant(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocationExpression = (InvocationExpressionSyntax)context.Node;
        var interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocationExpression, cancellationToken);
        var argumentsCount = invocationExpression.ArgumentList.Arguments.Count;
        if (argumentsCount == 0)
        {
            return new(AddOpenApiOverloadVariant.AddOpenApi, invocationExpression, interceptableLocation);
        }
        else if (argumentsCount == 2)
        {
            return new(AddOpenApiOverloadVariant.AddOpenApiDocumentNameConfigureOptions, invocationExpression, interceptableLocation);
        }
        else
        {
            // We need to disambiguate between the two overloads that take a string and a delegate
            // AddOpenApi("v1") vs. AddOpenApi(options => { })
            var argument = invocationExpression.ArgumentList.Arguments[0];
            if (argument.Expression is LiteralExpressionSyntax)
            {
                return new(AddOpenApiOverloadVariant.AddOpenApiDocumentName, invocationExpression, interceptableLocation);
            }
            else
            {
                return new(AddOpenApiOverloadVariant.AddOpenApiConfigureOptions, invocationExpression, interceptableLocation);
            }
        }
    }
}
