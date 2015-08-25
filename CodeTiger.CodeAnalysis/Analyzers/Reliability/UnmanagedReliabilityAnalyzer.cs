﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeTiger.CodeAnalysis.Analyzers.Reliability
{
    /// <summary>
    /// Analyzes the handling of unmanaged state for potential reliability issues.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class UnmanagedReliabilityAnalyzer : DiagnosticAnalyzer
    {
        internal static readonly DiagnosticDescriptor TypesWithDisposableStateShouldImplementIDisposableDescriptor
            = new DiagnosticDescriptor("CT2001", "Types with disposable state should implement IDisposable",
                "Types with disposable state should implement IDisposable", "CodeTiger.Reliability",
                DiagnosticSeverity.Warning, true);
        internal static readonly DiagnosticDescriptor
            TypesWithUnmanagedStateShouldImplementTheFullDisposePatternDescriptor = new DiagnosticDescriptor(
                "CT2002", "Types with unmanaged state should implement the full dispose pattern.",
                "Types with unmanaged state should implement the full dispose pattern.", "CodeTiger.Reliability",
                DiagnosticSeverity.Warning, true);
        internal static readonly DiagnosticDescriptor DestructorsShouldNotAccessManagedObjectsDescriptor
            = new DiagnosticDescriptor("CT2003", "Destructors should not access managed objects.",
                "Destructors should not access managed objects.", "CodeTiger.Reliability",
                DiagnosticSeverity.Warning, true);


        private static readonly string[] _unmanagedTypeMetadataNames = new string[]
            {
                "System.IntPtr",
                "System.UIntPtr",
                "System.Runtime.InteropServices.BINDPTR",
                "System.Runtime.InteropServices.ComTypes.BindPtr",
            };
        private static readonly string[] _metadataNamesOfDestructorSafeTypeNames = new string[]
            {
                "System.GC",
                "System.Runtime.InteropServices.Marshal",
            };

        /// <summary>
        /// Gets a set of descriptors for the diagnostics that this analyzer is capable of producing.
        /// </summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(TypesWithDisposableStateShouldImplementIDisposableDescriptor,
                    TypesWithUnmanagedStateShouldImplementTheFullDisposePatternDescriptor,
                    DestructorsShouldNotAccessManagedObjectsDescriptor);
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

            context.RegisterSemanticModelAction(AnalyzeUnmanagedState);
        }

        private static void AnalyzeUnmanagedState(SemanticModelAnalysisContext context)
        {
            var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);

            var disposableType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.IDisposable");

            foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var instanceStateMembers = typeDeclaration.Members.Where(IsInstanceState);
                var instanceStateMemberTypes = instanceStateMembers.Select(GetMemberType);

                bool isTypeDisposable = IsTypeDisposable(context, typeDeclaration, disposableType);

                if (!isTypeDisposable && AreAnyTypesDisposable(context, instanceStateMemberTypes, disposableType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        TypesWithDisposableStateShouldImplementIDisposableDescriptor,
                        typeDeclaration.Identifier.GetLocation()));
                }

                var destructor = typeDeclaration.Members
                    .FirstOrDefault(x => x.Kind() == SyntaxKind.DestructorDeclaration);

                if ((!isTypeDisposable || destructor == null)
                    && AreAnyTypesUnmanaged(context, instanceStateMemberTypes))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        TypesWithUnmanagedStateShouldImplementTheFullDisposePatternDescriptor,
                        typeDeclaration.Identifier.GetLocation()));
                }

                if (destructor != null)
                {
                    AnalyzeDestructorForManagedObjects(context, destructor);
                }
            }
        }

        private static void AnalyzeDestructorForManagedObjects(SemanticModelAnalysisContext context,
            MemberDeclarationSyntax destructorDeclaration)
        {
            var destructorSymbol = context.SemanticModel
                .GetDeclaredSymbol(destructorDeclaration, context.CancellationToken);
            if (destructorSymbol == null)
            {
                return;
            }

            foreach (var node in destructorDeclaration.DescendantNodes())
            {
                SyntaxNode accessedNode;
                ISymbol accessedSymbol;

                switch (node.Kind())
                {
                    case SyntaxKind.PointerMemberAccessExpression:
                    case SyntaxKind.SimpleMemberAccessExpression:
                        accessedNode = node;
                        accessedSymbol = context.SemanticModel
                            .GetSymbolInfo(accessedNode, context.CancellationToken).Symbol;
                        break;
                    case SyntaxKind.InvocationExpression:
                        accessedNode = ((InvocationExpressionSyntax)node).Expression;
                        accessedSymbol = context.SemanticModel
                            .GetSymbolInfo(accessedNode, context.CancellationToken).Symbol?.ContainingSymbol;
                        break;
                    default:
                        accessedNode = null;
                        accessedSymbol = null;
                        break;
                }

                ITypeSymbol accessedTypeSymbol = accessedSymbol as ITypeSymbol;

                if (accessedNode != null
                    && accessedTypeSymbol != null
                    && accessedTypeSymbol != destructorSymbol.ContainingType
                    && IsTypeProbablyUnsafeToAccessFromDestructor(context, accessedTypeSymbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DestructorsShouldNotAccessManagedObjectsDescriptor, accessedNode.GetLocation()));
                }
            }
        }

        private static bool IsInstanceState(MemberDeclarationSyntax memberDeclaration)
        {
            switch (memberDeclaration.Kind())
            {
                case SyntaxKind.FieldDeclaration:
                    {
                        var fieldDeclaration = (BaseFieldDeclarationSyntax)memberDeclaration;
                        return !fieldDeclaration.Modifiers.Any(x => x.Kind() == SyntaxKind.StaticKeyword);
                    }
                case SyntaxKind.PropertyDeclaration:
                    {
                        var propertyDeclaration = (BasePropertyDeclarationSyntax)memberDeclaration;
                        return !propertyDeclaration.Modifiers.Any(x => x.Kind() == SyntaxKind.StaticKeyword)
                            && propertyDeclaration.AccessorList.Accessors.All(x => x.Body == null);
                    }
                default:
                    return false;
            }
        }

        private static TypeSyntax GetMemberType(MemberDeclarationSyntax memberDeclaration)
        {
            switch (memberDeclaration.Kind())
            {
                case SyntaxKind.FieldDeclaration:
                    {
                        var fieldDeclaration = (BaseFieldDeclarationSyntax)memberDeclaration;
                        return fieldDeclaration.Declaration.Type;
                    }
                case SyntaxKind.PropertyDeclaration:
                    {
                        var propertyDeclaration = (BasePropertyDeclarationSyntax)memberDeclaration;
                        return propertyDeclaration.Type;
                    }
                default:
                    throw new ArgumentException("The argument must be a field or property.",
                        nameof(memberDeclaration));
            }
        }

        private static bool AreAnyTypesUnmanaged(SemanticModelAnalysisContext context,
            IEnumerable<TypeSyntax> instanceStateMemberTypes)
        {
            if (instanceStateMemberTypes == null || !instanceStateMemberTypes.Any())
            {
                return false;
            }

            var unmanagedTypes = _unmanagedTypeMetadataNames
                .Select(context.SemanticModel.Compilation.GetTypeByMetadataName)
                .Where(x => x != null)
                .ToList();

            foreach (var instanceMemberType in instanceStateMemberTypes)
            {
                var memberType = context.SemanticModel.GetTypeInfo(instanceMemberType, context.CancellationToken);
                if (unmanagedTypes.Any(x => x.Equals(memberType.Type)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreAnyTypesDisposable(SemanticModelAnalysisContext context,
            IEnumerable<TypeSyntax> instanceStateMemberTypes, INamedTypeSymbol disposableType)
        {
            if (instanceStateMemberTypes == null || !instanceStateMemberTypes.Any())
            {
                return false;
            }

            return instanceStateMemberTypes
                .Select(x => context.SemanticModel.GetTypeInfo(x, context.CancellationToken).Type)
                .Any(x => x.Equals(disposableType) || x.AllInterfaces.Any(disposableType.Equals));
        }

        private static bool IsTypeDisposable(SemanticModelAnalysisContext context,
            TypeDeclarationSyntax classDeclaration, INamedTypeSymbol disposableType)
        {
            if (disposableType == null)
            {
                return false;
            }
            
            return classDeclaration.BaseList != null
                && classDeclaration.BaseList.Types
                    .Select(x => context.SemanticModel.GetSymbolInfo(x.Type, context.CancellationToken).Symbol)
                    .Any(disposableType.Equals);
        }

        private static bool IsTypeProbablyUnsafeToAccessFromDestructor(SemanticModelAnalysisContext context,
            ITypeSymbol accessedTypeSymbol)
        {
            if (accessedTypeSymbol.IsValueType)
            {
                return false;
            }

            foreach (var destructorSafeTypeName in _metadataNamesOfDestructorSafeTypeNames)
            {
                var destructorSafeType = context.SemanticModel.Compilation
                    .GetTypeByMetadataName(destructorSafeTypeName);
                if (destructorSafeType != null && destructorSafeType.Equals(accessedTypeSymbol))
                {
                    return false;
                }
            }

            return true;
        }
    }
}