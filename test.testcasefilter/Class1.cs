using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace test.testcasefilter
{
    public class Tests
    {
        [Fact]
        [Trait("FilterCategory", "Exclude")]
        public void TestWithTraitToFilterOn()
        {
            Assert.True(true);
        }
    }
}
