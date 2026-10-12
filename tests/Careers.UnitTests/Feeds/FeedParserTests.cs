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

    [Fact]
    public void Jobicy_reads_offers_with_level_and_type_as_tags_and_skips_unsafe_links()
    {
        var offers = JobicyFeed.Parse(Bytes("""
        {"jobs":[
          {"id":155104,"url":"https://jobicy.com/jobs/155104-x","jobTitle":"Backend Engineer","companyName":"Acme",
           "jobGeo":"LATAM","jobLevel":"Senior","jobType":["Full-Time"],"jobIndustry":["Software"],
           "jobDescription":"<p>Build APIs</p>","pubDate":"2026-10-12T13:39:32+00:00"},
          {"id":2,"url":"ftp://jobicy.com/2","jobTitle":"Evil","companyName":"X"}
        ]}
        """), Now);

        var offer = Assert.Single(offers);
        Assert.Equal("jobicy", offer.Source);
        Assert.Equal("155104", offer.SourceId);
        Assert.Equal("LATAM", offer.Location);
        Assert.Contains("Senior", offer.Tags);
        Assert.Equal("Build APIs", offer.Description);
    }

    [Fact]
    public void Himalayas_without_restrictions_is_worldwide_and_the_page_url_is_its_identity()
    {
        var offers = HimalayasFeed.Parse(Bytes("""
        {"jobs":[
          {"title":"Software Developer","companyName":"SIL","locationRestrictions":[],"categories":["Programming"],
           "seniority":["Mid-level"],"description":"<h3>Job</h3>Code","pubDate":1789615675,
           "applicationLink":"https://himalayas.app/companies/sil/jobs/software-developer",
           "guid":"https://himalayas.app/companies/sil/jobs/software-developer"},
          {"title":"Restricted","companyName":"X","locationRestrictions":["Chile","Peru"],
           "guid":"https://himalayas.app/companies/x/jobs/restricted"},
          {"title":"No link","companyName":"X"}
        ]}
        """), Now);

        Assert.Equal(2, offers.Count);
        Assert.Equal("Worldwide", offers[0].Location);
        Assert.Equal("https://himalayas.app/companies/sil/jobs/software-developer", offers[0].SourceId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789615675), offers[0].PublishedAt);
        Assert.Equal("Chile, Peru", offers[1].Location);
    }

    [Fact]
    public void GetOnBoard_joins_the_description_sections_and_names_modality_and_countries()
    {
        var offers = GetOnBoardFeed.Parse(Bytes("""
        {"data":[
          {"id":"dev-1","links":{"public_url":"https://www.getonbrd.com/jobs/dev-1"},
           "attributes":{"title":"Developer Front-end","functions":"<p>Build UIs</p>","description":"<ul><li>React</li></ul>",
             "remote_modality":"hybrid","countries":["Chile"],"published_at":1791590800},
           "relationships":{}},
          {"id":"dev-2","attributes":{"title":"No link"}}
        ]}
        """), Now);

        var offer = Assert.Single(offers);
        Assert.Equal("getonbrd", offer.Source);
        Assert.Equal("Hybrid · Chile", offer.Location);
        Assert.Contains("Build UIs", offer.Description);
        Assert.Contains("React", offer.Description);
    }

    [Fact]
    public void LinkedIn_through_Apify_accepts_the_usual_field_spellings_and_drops_duplicates()
    {
        var offers = ApifyLinkedInFeed.Parse(Bytes("""
        [{"id":"4001","link":"https://www.linkedin.com/jobs/view/4001","title":"Desarrollador .NET","companyName":"Acme",
          "location":"Quito, Ecuador","descriptionText":"Requisitos: C#","postedAt":"2026-10-10"},
         {"id":"4001","link":"https://www.linkedin.com/jobs/view/4001","title":"Desarrollador .NET (repetida)"},
         {"jobId":"4002","jobUrl":"https://www.linkedin.com/jobs/view/4002","jobTitle":"Backend","company":"Beta"},
         {"id":"4003","link":"javascript:alert(1)","title":"Evil"}]
        """), Now);

        Assert.Equal(2, offers.Count);
        Assert.Equal("linkedin", offers[0].Source);
        Assert.Equal("Quito, Ecuador", offers[0].Location);
        Assert.Equal("Requisitos: C#", offers[0].Description);
        Assert.Equal("Beta", offers[1].Company);
        Assert.Empty(ApifyLinkedInFeed.Parse(Bytes("""{"error":"x"}"""), Now));
    }
}
