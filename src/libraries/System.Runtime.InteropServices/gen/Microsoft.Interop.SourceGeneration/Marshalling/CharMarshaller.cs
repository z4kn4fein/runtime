﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Interop
{
    public sealed class Utf16CharMarshaller : IMarshallingGenerator
    {
        private static readonly PredefinedTypeSyntax s_nativeType = PredefinedType(Token(SyntaxKind.UShortKeyword));

        public Utf16CharMarshaller()
        {
        }

        public bool IsSupported(TargetFramework target, Version version) => true;

        public ArgumentSyntax AsArgument(TypePositionInfo info, StubCodeContext context)
        {
            (string managedIdentifier, string nativeIdentifier) = context.GetIdentifiers(info);
            if (!info.IsByRef)
            {
                // (ushort)<managedIdentifier>
                return Argument(
                    CastExpression(
                        AsNativeType(info),
                        IdentifierName(managedIdentifier)));
            }
            else if (IsPinningPathSupported(info, context))
            {
                // (ushort*)<pinned>
                return Argument(
                    CastExpression(
                        PointerType(AsNativeType(info)),
                        IdentifierName(PinnedIdentifier(info.InstanceIdentifier))));
            }

            // &<nativeIdentifier>
            return Argument(
                PrefixUnaryExpression(
                    SyntaxKind.AddressOfExpression,
                    IdentifierName(nativeIdentifier)));
        }

        public TypeSyntax AsNativeType(TypePositionInfo info)
        {
            Debug.Assert(info.ManagedType is SpecialTypeInfo(_, _, SpecialType.System_Char));
            return s_nativeType;
        }

        public ParameterSyntax AsParameter(TypePositionInfo info)
        {
            TypeSyntax type = info.IsByRef
                ? PointerType(AsNativeType(info))
                : AsNativeType(info);
            return Parameter(Identifier(info.InstanceIdentifier))
                .WithType(type);
        }

        public IEnumerable<StatementSyntax> Generate(TypePositionInfo info, StubCodeContext context)
        {
            (string managedIdentifier, string nativeIdentifier) = context.GetIdentifiers(info);

            if (IsPinningPathSupported(info, context))
            {
                if (context.CurrentStage == StubCodeContext.Stage.Pin)
                {
                    // fixed (char* <pinned> = &<managed>)
                    yield return FixedStatement(
                        VariableDeclaration(
                            PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                            SingletonSeparatedList(
                                VariableDeclarator(Identifier(PinnedIdentifier(info.InstanceIdentifier)))
                                    .WithInitializer(EqualsValueClause(
                                        PrefixUnaryExpression(
                                            SyntaxKind.AddressOfExpression,
                                            IdentifierName(Identifier(managedIdentifier)))
                                    ))
                            )
                        ),
                        EmptyStatement()
                    );
                }
                yield break;
            }

            switch (context.CurrentStage)
            {
                case StubCodeContext.Stage.Setup:
                    break;
                case StubCodeContext.Stage.Marshal:
                    if ((info.IsByRef && info.RefKind != RefKind.Out) || !context.SingleFrameSpansNativeContext)
                    {
                        yield return ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                IdentifierName(nativeIdentifier),
                                IdentifierName(managedIdentifier)));
                    }

                    break;
                case StubCodeContext.Stage.Unmarshal:
                    if (info.IsManagedReturnPosition || (info.IsByRef && info.RefKind != RefKind.In))
                    {
                        yield return ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                IdentifierName(managedIdentifier),
                                CastExpression(
                                    PredefinedType(
                                        Token(SyntaxKind.CharKeyword)),
                                    IdentifierName(nativeIdentifier))));
                    }

                    break;
                default:
                    break;
            }
        }

        public bool UsesNativeIdentifier(TypePositionInfo info, StubCodeContext context)
        {
            return info.IsManagedReturnPosition || (info.IsByRef && !context.SingleFrameSpansNativeContext);
        }

        public bool SupportsByValueMarshalKind(ByValueContentsMarshalKind marshalKind, StubCodeContext context) => false;

        private bool IsPinningPathSupported(TypePositionInfo info, StubCodeContext context)
        {
            return context.SingleFrameSpansNativeContext
                && !info.IsManagedReturnPosition
                && info.IsByRef;
        }

        private static string PinnedIdentifier(string identifier) => $"{identifier}__pinned";
    }
}
