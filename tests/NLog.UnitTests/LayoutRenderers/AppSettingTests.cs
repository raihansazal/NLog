// 
// Copyright (c) 2004-2018 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

#if !NETSTANDARD

namespace NLog.UnitTests.LayoutRenderers
{
    using System.Collections.Specialized;
    using NLog.Internal;
    using NLog.LayoutRenderers;
	using Xunit;

    public class AppSettingTests : NLogTestBase
    {
        [Fact]
        public void UseAppSettingTest()
        {
            var configurationManager = new MockConfigurationManager();
            const string expected = "appSettingTestValue";
            configurationManager.AppSettings["appSettingTestKey"] = expected;
            var appSettingLayoutRenderer = new AppSettingLayoutRenderer2
            {
                ConfigurationManager = configurationManager,
                Name = "appSettingTestKey",
            };

            var rendered = appSettingLayoutRenderer.Render(LogEventInfo.CreateNullEvent());

            Assert.Equal(expected, rendered);
        }

        [Fact]
        public void AppSettingOverridesDefaultTest()
        {
            var configurationManager = new MockConfigurationManager();
            const string expected = "appSettingTestValue";
            configurationManager.AppSettings["appSettingTestKey"] = expected;
            var appSettingLayoutRenderer = new AppSettingLayoutRenderer2
            {
                ConfigurationManager = configurationManager,
                Name = "appSettingTestKey",
                Default = "UseDefault",
            };

            var rendered = appSettingLayoutRenderer.Render(LogEventInfo.CreateNullEvent());

            Assert.Equal(expected, rendered);
        }

        [Fact]
        public void FallbackToDefaultTest()
        {
            var configurationManager = new MockConfigurationManager();
            const string expected = "UseDefault";
            var appSettingLayoutRenderer = new AppSettingLayoutRenderer2
            {
                ConfigurationManager = configurationManager,
                Name = "notFound",
                Default = "UseDefault",
            };

            var rendered = appSettingLayoutRenderer.Render(LogEventInfo.CreateNullEvent());

            Assert.Equal(expected, rendered);
        }

        [Fact]
        public void NoAppSettingTest()
        {
            var configurationManager = new MockConfigurationManager();
            var appSettingLayoutRenderer = new AppSettingLayoutRenderer2
            {
                ConfigurationManager = configurationManager,
                Name = "notFound",
            };

            var rendered = appSettingLayoutRenderer.Render(LogEventInfo.CreateNullEvent());

            Assert.Equal(string.Empty, rendered);
        }

        private class MockConfigurationManager : IConfigurationManager
        {
            public MockConfigurationManager()
            {
                AppSettings = new NameValueCollection();
            }

            public NameValueCollection AppSettings { get; private set; }
        }
    }
}

#endif