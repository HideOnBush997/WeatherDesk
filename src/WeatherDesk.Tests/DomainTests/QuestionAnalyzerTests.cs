using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class QuestionAnalyzerTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    [Fact]
    public void Extracts_city_date_and_running_intent()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "明天上海适合跑步吗",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("上海", analysis.City);
        Assert.Equal(new DateOnly(2026, 10, 3), analysis.Date);
        Assert.Equal(AgentIntent.Running, analysis.Intent);
        Assert.True(analysis.RequiresWeather);
    }

    [Fact]
    public void Extracts_date_and_commute_intent_without_guessing_a_city()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "今天适合通勤吗",
            new AgentContext(DomainTestData.CreateLocation(), TimeSpan.FromHours(8), false)
            {
                Today = Today
            });

        Assert.Null(analysis.City);
        Assert.Equal(Today, analysis.Date);
        Assert.Equal(AgentIntent.Commute, analysis.Intent);
        Assert.True(analysis.RequiresWeather);
    }

    [Fact]
    public void Extracts_a_city_without_a_date()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "在东京下雨吗",
            new AgentContext(null, TimeSpan.Zero, false) { Today = Today });

        Assert.Equal("东京", analysis.City);
        Assert.Null(analysis.Date);
        Assert.Equal(AgentIntent.Umbrella, analysis.Intent);
    }

    [Fact]
    public void Extracts_city_from_a_weather_status_question()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "上海天气怎么样",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("上海", analysis.City);
        Assert.Equal(AgentIntent.General, analysis.Intent);
    }

    [Fact]
    public void Treats_outdoor_plan_question_as_weather_commute_intent()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "武汉明天适合出门吗",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("武汉", analysis.City);
        Assert.Equal(AgentIntent.Commute, analysis.Intent);
        Assert.True(analysis.RequiresWeather);
    }

    [Fact]
    public void Treats_multi_day_travel_plan_as_weather_question()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "我要去武汉旅游7天，我要注意什么",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("武汉", analysis.City);
        Assert.Equal(AgentIntent.Commute, analysis.Intent);
        Assert.Equal(Today, analysis.Date);
        Assert.True(analysis.RequiresWeather);
    }

    [Fact]
    public void Extracts_origin_and_destination_for_route_question()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "我明天要从武汉去上海，要怎么穿衣服呢",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("武汉", analysis.OriginCity);
        Assert.Equal("上海", analysis.DestinationCity);
        Assert.Equal("上海", analysis.City);
        Assert.Equal(AgentIntent.Clothing, analysis.Intent);
    }

    [Fact]
    public void Extracts_route_when_question_has_no_punctuation()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "明天从武汉去上海适合穿什么",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("武汉", analysis.OriginCity);
        Assert.Equal("上海", analysis.DestinationCity);
    }

    [Fact]
    public void Extracts_route_without_explicit_from_preposition()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "武汉去北京",
            new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("武汉", analysis.OriginCity);
        Assert.Equal("北京", analysis.DestinationCity);
        Assert.Equal(AgentIntent.Commute, analysis.Intent);
        Assert.True(analysis.RequiresWeather);
    }

    [Fact]
    public void Extracts_multiple_destinations_from_natural_question()
    {
        var analysis = QuestionAnalyzer.Analyze("明天我要去武汉和北京，出门要注意什么", new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });
        Assert.Equal("武汉", analysis.City);
        Assert.Contains("北京", analysis.AdditionalCities!);
        Assert.Equal(AgentIntent.Commute, analysis.Intent);
    }

    [Fact]
    public void Extracts_locations_when_question_lists_places_without_route_words()
    {
        var analysis = QuestionAnalyzer.Analyze("我问海南，哈尔滨和乌鲁木齐", new AgentContext(null, TimeSpan.FromHours(8), false) { Today = Today });

        Assert.Equal("海南", analysis.City);
        Assert.Contains("哈尔滨", analysis.AdditionalCities!);
        Assert.Contains("乌鲁木齐", analysis.AdditionalCities!);
    }

    [Fact]
    public void Relative_date_is_unresolved_when_today_is_not_supplied()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "明天适合跑步吗",
            new AgentContext(null, TimeSpan.Zero, false));

        Assert.Null(analysis.Date);
        Assert.Equal(AgentIntent.Running, analysis.Intent);
    }

    [Fact]
    public void Non_weather_question_is_not_marked_as_weather_related()
    {
        var analysis = QuestionAnalyzer.Analyze(
            "帮我写一首诗",
            new AgentContext(null, TimeSpan.Zero, false) { Today = Today });

        Assert.Equal(AgentIntent.Unknown, analysis.Intent);
        Assert.False(analysis.RequiresWeather);
        Assert.Null(analysis.City);
        Assert.Null(analysis.Date);
    }
}
