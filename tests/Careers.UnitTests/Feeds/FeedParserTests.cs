using System.Text;
using Careers.Infrastructure.Feeds;

namespace Careers.UnitTests.Feeds;

public class FeedParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Remotive_jobs_become_offers_and_unusable_ones_are_skipped()
    {
        var offers = RemotiveFeed.Parse(Bytes("""
        {"jobs":[
          {"id":1,"url":"https://remotive.com/j/1","title":"Backend Engineer","company_name":"Acme",
           "candidate_required_location":"Worldwide","tags":["dotnet","sql"],
           "description":"<p>Build <b>APIs</b></p><script>x()</script>","publication_date":"2026-09-30T10:00:00"},
          {"id":2,"url":"javascript:alert(1)","title":"Evil","company_name":"X"},
          {"id":3,"url":"https://remotive.com/j/3","title":"","company_name":"X"},
          {"url":"https://remotive.com/j/4","title":"No id","company_name":"X"}
        ]}
        """), Now);

        var offer = Assert.Single(offers);
        Assert.Equal("remotive", offer.Source);
        Assert.Equal("1", offer.SourceId);
        Assert.Equal("Build APIs", offer.Description);
        Assert.Equal(["dotnet", "sql"], offer.Tags);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero), offer.PublishedAt);
    }

    [Fact]
    public void Remotive_with_an_unexpected_shape_yields_nothing()
    {
        Assert.Empty(RemotiveFeed.Parse(Bytes("""{"other":1}"""), Now));
        Assert.Empty(RemotiveFeed.Parse(Bytes("""{"jobs":"nope"}"""), Now));
    }

    [Fact]
    public void RemoteOk_skips_its_legal_notice_and_reads_the_offers()
    {
        var offers = RemoteOkFeed.Parse(Bytes("""
        [{"legal":"Please link back"},
         {"id":"9","url":"https://remoteok.com/9","position":"backend engineer","company":"ACME","tags":["python"],
          "description":"<ul><li>SQL</li></ul>","date":"2026-09-30T18:24:42+00:00","location":"Remote"}]
        """), Now);

        var offer = Assert.Single(offers);
        Assert.Equal("backend engineer", offer.Title);
        Assert.Equal("SQL", offer.Description);
        Assert.Equal("ACME", offer.Company);
    }

    [Fact]
    public void WeWorkRemotely_splits_company_and_title_and_unescapes_the_description()
    {
        var offers = WeWorkRemotelyFeed.Parse(Bytes("""
        <?xml version="1.0"?><rss><channel><item>
          <title>Acme: Backend Engineer</title><region>Anywhere</region>
          <link>https://weworkremotely.com/x</link><guid>x1</guid>
          <pubDate>Tue, 30 Sep 2026 10:00:00 +0000</pubDate>
          <description>&lt;p&gt;Hola &amp;amp; adios&lt;/p&gt;</description>
        </item></channel></rss>
        """), Now);

        var offer = Assert.Single(offers);
        Assert.Equal("Acme", offer.Company);
        Assert.Equal("Backend Engineer", offer.Title);
        Assert.Equal("Hola & adios", offer.Description);
    }

    [Fact]
    public void A_feed_with_a_dtd_is_refused()
    {
        var bomb = """
        <?xml version="1.0"?><!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;">]>
        <rss><channel><item><title>&lol2;</title><link>https://x.example/1</link></item></channel></rss>
        """;

        Assert.ThrowsAny<System.Xml.XmlException>(() => WeWorkRemotelyFeed.Parse(Bytes(bomb), Now));
    }
}
