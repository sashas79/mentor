using Mentor.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddMentorApiHost();

var app = builder.Build();
app.UseMentorApiHost();

app.Run();
