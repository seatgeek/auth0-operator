using System.Collections;
using System.Collections.Generic;

using Alethic.Auth0.Operator.Controllers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Locks the materialize-once contract on
    /// <see cref="LogValueFormatter.FormatValueForLogging"/> for the <c>IEnumerable</c> branch.
    /// Previous implementation called <c>.Take(5).Select(...)</c> and <c>.Count()</c> on the same
    /// source, which re-enumerates lazy / one-shot enumerables. Regression-locked via
    /// <see cref="OneShotEnumerable"/>, which throws if its <c>GetEnumerator()</c> is called twice.
    /// </summary>
    [TestClass]
    public class LogValueFormatterTests
    {
        [TestMethod]
        public void FormatValueForLogging_IEnumerable_EnumeratesSourceOnlyOnce()
        {
            var source = new OneShotEnumerable(new object[] { "a", "b", "c" });

            var result = LogValueFormatter.FormatValueForLogging(source);

            Assert.AreEqual("[\"a\", \"b\", \"c\"]", result);
        }

        [TestMethod]
        public void FormatValueForLogging_IEnumerable_OverFiveItems_EnumeratesSourceOnlyOnce()
        {
            var source = new OneShotEnumerable(new object[] { 1, 2, 3, 4, 5, 6, 7 });

            var result = LogValueFormatter.FormatValueForLogging(source);

            StringAssert.Contains(result, "(total: 7 items)");
        }

        /// <summary>
        /// Single-pass <see cref="IEnumerable"/> test helper. Throws on the second
        /// <see cref="GetEnumerator"/> call so any double-enumeration in production code fails fast.
        /// </summary>
        private sealed class OneShotEnumerable : IEnumerable
        {
            private readonly IEnumerable _inner;
            private bool _enumerated;

            public OneShotEnumerable(IEnumerable inner)
            {
                _inner = inner;
            }

            public IEnumerator GetEnumerator()
            {
                if (_enumerated)
                    Assert.Fail("OneShotEnumerable enumerated more than once — caller is double-enumerating the source.");
                _enumerated = true;
                return _inner.GetEnumerator();
            }
        }
    }
}
