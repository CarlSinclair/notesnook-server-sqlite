/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Streetwriters.Common;

namespace Notesnook.API.Authorization
{
    /// <summary>
    /// Route-level feature toggle. When the named feature is disabled the action is
    /// invisible to routing (→ 404), so a single binary serves every configuration.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RequiresFeatureAttribute(string feature) : Attribute, IActionConstraint
    {
        public int Order => -1;

        public bool Accept(ActionConstraintContext context) => feature switch
        {
            "monographs" => Constants.ENABLE_MONOGRAPHS,
            "inbox" => Constants.ENABLE_INBOX,
            _ => true,
        };
    }
}
