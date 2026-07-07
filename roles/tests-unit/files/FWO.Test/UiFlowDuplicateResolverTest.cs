using BlazorTable;
using Bunit;
using FWO.Config.Api;
using FWO.Data;
using FWO.Ui.Shared;
using FWO.Ui.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace FWO.Test
{
    [TestFixture]
    internal class UiFlowDuplicateResolverTest
    {
        [Test]
        public void SaveButton_IsEnabledWhenInitialSelectionExists()
        {
            using BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddBlazorTable();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig());

            List<NetworkObject> items =
            [
                new NetworkObject
                {
                    Id = 1,
                    Name = "one",
                    IP = "",
                    IpEnd = "",
                    Uid = "uid-1",
                    FlowActive = false
                }
            ];

            IRenderedComponent<FlowDuplicateResolver<NetworkObject>> cut = context.Render<FlowDuplicateResolver<NetworkObject>>(parameters => parameters
                .Add(p => p.Title, "Duplicate objects")
                .Add(p => p.Show, true)
                .Add(p => p.Size, PopupSize.Auto)
                .Add(p => p.Items, items)
                .Add(p => p.OnResolve, _ => Task.CompletedTask)
                .Add(p => p.SummaryContent, (RenderFragment)(builder => builder.AddMarkupContent(0, "<div>Flow object: test</div>"))));

            Assert.That(cut.Markup, Does.Contain("Duplicate objects"));
            Assert.That(cut.Markup, Does.Contain("Flow object: test"));
            Assert.That(cut.Markup, Does.Contain("Save"));
            Assert.That(cut.Markup, Does.Contain("Cancel"));
            Assert.That(cut.Find("button.btn.btn-sm.btn-warning").GetAttribute("disabled"), Is.Null);
        }

        [Test]
        public void SaveButton_InvokesResolveWithSelectedItem()
        {
            using BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddBlazorTable();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig());

            List<NetworkObject> items =
            [
                new NetworkObject
                {
                    Id = 1,
                    Name = "one",
                    IP = "",
                    IpEnd = "",
                    Uid = "uid-1",
                    FlowActive = false
                },
                new NetworkObject
                {
                    Id = 2,
                    Name = "two",
                    IP = "",
                    IpEnd = "",
                    Uid = "uid-2",
                    FlowActive = false
                }
            ];

            NetworkObject? resolvedItem = null;

            IRenderedComponent<FlowDuplicateResolver<NetworkObject>> cut = context.Render<FlowDuplicateResolver<NetworkObject>>(parameters => parameters
                .Add(p => p.Title, "Duplicate objects")
                .Add(p => p.Show, true)
                .Add(p => p.Size, PopupSize.Auto)
                .Add(p => p.Items, items)
                .Add(p => p.OnResolve, item =>
                {
                    resolvedItem = item;
                    return Task.CompletedTask;
                })
                .Add(p => p.SummaryContent, (RenderFragment)(builder => builder.AddMarkupContent(0, "<div>Flow object: test</div>"))));

            cut.FindAll("button.btn.btn-sm.btn-outline-primary").Last().Click();
            cut.Find("button.btn.btn-sm.btn-warning").Click();

            Assert.That(resolvedItem, Is.Not.Null);
            Assert.That(resolvedItem!.Id, Is.EqualTo(2));
        }

        [Test]
        public void RendersSelectedRowClassFromInternalSelection()
        {
            using BunitContext context = new();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddBlazorTable();
            context.Services.AddSingleton<UserConfig>(new SimulatedUserConfig());

            List<NetworkObject> items =
            [
                new NetworkObject
                {
                    Id = 1,
                    Name = "one",
                    IP = "",
                    IpEnd = "",
                    Uid = "uid-1",
                    FlowActive = false
                },
                new NetworkObject
                {
                    Id = 2,
                    Name = "two",
                    IP = "",
                    IpEnd = "",
                    Uid = "uid-2",
                    FlowActive = false
                }
            ];

            IRenderedComponent<FlowDuplicateResolver<NetworkObject>> cut = context.Render<FlowDuplicateResolver<NetworkObject>>(parameters => parameters
                .Add(p => p.Title, "Duplicate objects")
                .Add(p => p.Show, true)
                .Add(p => p.Size, PopupSize.Auto)
                .Add(p => p.Items, items)
                .Add(p => p.OnClose, () => { }));

            cut.FindAll("button.btn.btn-sm.btn-outline-primary").Last().Click();
            Assert.That(cut.Markup, Does.Contain("table-warning"));
        }
    }
}
