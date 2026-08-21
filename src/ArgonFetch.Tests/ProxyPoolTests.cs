using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class ProxyPoolTests
    {
        [Fact]
        public void Next_ReturnsNull_WhenNoProxiesConfigured()
        {
            var pool = new ProxyPool(ProxyPool.ReadList(null));

            Assert.Equal(0, pool.Count);
            Assert.Null(pool.Next());
        }

        [Fact]
        public void ReadList_SkipsBlankLinesAndComments()
        {
            var path = Path.GetTempFileName();
            File.WriteAllLines(path, ["# comment", "", "  http://a:1  ", "socks5://b:2"]);

            try
            {
                Assert.Equal(["http://a:1", "socks5://b:2"], ProxyPool.ReadList(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ReadList_NormalizesProviderExportsToProxyUrls()
        {
            var path = Path.GetTempFileName();
            File.WriteAllLines(path, ["1.2.3.4:8080", "1.2.3.4:8080:user:secret"]);

            try
            {
                Assert.Equal(
                    ["http://1.2.3.4:8080", "http://user:secret@1.2.3.4:8080"],
                    ProxyPool.ReadList(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Next_RotatesThroughTheListAndWrapsAround()
        {
            var pool = new ProxyPool(["a", "b", "c"]);

            Assert.Equal(["a", "b", "c", "a"], Enumerable.Range(0, 4).Select(_ => pool.Next()));
        }
    }
}
