﻿using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeTiger.CodeAnalysis.Analyzers.Design
{
    /// <summary>
    /// Analyzes design issues related to parameters.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ParameterDesignAnalyzer : DiagnosticAnalyzer
    {
        internal static readonly DiagnosticDescriptor MethodsShouldNotExceedSevenParametersDescriptor
            = new DiagnosticDescriptor("CT1003", "Methods should not exceed seven parameters.",
                "Methods should not exceed seven parameters.", "CodeTiger.Design", DiagnosticSeverity.Warning,
                true);

        /// <summary>
        /// Gets a set of descriptors for the diagnostics that this analyzer is capable of producing.
        /// </summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(MethodsShouldNotExceedSevenParametersDescriptor);
            }
        }

        /// <summary>
        /// Registers actions in an analysis context.
        /// </summary>
        /// <param name="context">The context to register actions in.</param>
        /// <remarks>This method should only be called once, at the start of a session.</remarks>
        public override void Initialize(AnalysisContext context)
        {
            Guard.ArgumentIsNotNull(nameof(context), context);

            context.RegisterSyntaxNodeAction(AnalyzeParameterCount, SyntaxKind.ConstructorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration, SyntaxKind.DelegateDeclaration,
                SyntaxKind.IndexerDeclaration, SyntaxKind.MethodDeclaration,
                SyntaxKind.OperatorDeclaration);
        }

        private void AnalyzeParameterCount(SyntaxNodeAnalysisContext context)
        {
            if (GetParameterCount(context.Node) > 7)
            {
                context.ReportDiagnostic(Diagnostic.Create(MethodsShouldNotExceedSevenParametersDescriptor,
                    GetIdentifierLocation(context.Node)));
            }
        }

        private static int GetParameterCount(SyntaxNode node)
        {
            BaseParameterListSyntax parameterList;

            switch (node.Kind())
            {
                case SyntaxKind.ConstructorDeclaration:
                    parameterList = ((ConstructorDeclarationSyntax)node).ParameterList;
                    break;
                case SyntaxKind.ConversionOperatorDeclaration:
                    parameterList = ((ConversionOperatorDeclarationSyntax)node).ParameterList;
                    break;
                case SyntaxKind.DelegateDeclaration:
                    parameterList = ((DelegateDeclarationSyntax)node).ParameterList;
                    break;
                case SyntaxKind.IndexerDeclaration:
                    parameterList = ((IndexerDeclarationSyntax)node).ParameterList;
                    break;
                case SyntaxKind.MethodDeclaration:
                    parameterList = ((MethodDeclarationSyntax)node).ParameterList;
                    break;
                case SyntaxKind.OperatorDeclaration:
                    parameterList = ((OperatorDeclarationSyntax)node).ParameterList;
                    break;
                default:
                    return 0;
            }

            return parameterList?.Parameters.Count ?? 0;
        }

        private static Location GetIdentifierLocation(SyntaxNode node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.ConstructorDeclaration:
                    return ((ConstructorDeclarationSyntax)node).Identifier.GetLocation();
                case SyntaxKind.ConversionOperatorDeclaration:
                    return ((ConversionOperatorDeclarationSyntax)node).Type.GetLocation();
                case SyntaxKind.DelegateDeclaration:
                    return ((DelegateDeclarationSyntax)node).Identifier.GetLocation();
                case SyntaxKind.IndexerDeclaration:
                    return ((IndexerDeclarationSyntax)node).ThisKeyword.GetLocation();
                case SyntaxKind.MethodDeclaration:
                    return ((MethodDeclarationSyntax)node).Identifier.GetLocation();
                case SyntaxKind.OperatorDeclaration:
                    return ((OperatorDeclarationSyntax)node).OperatorToken.GetLocation();
                default:
                    return null;
            }
        }
    }
}