using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Content.Shared.MassMedia.Systems;

namespace Content.Client.MassMedia.Ui;

[GenerateTypedNameReferences]
public sealed partial class NewsWriteMenu : DefaultWindow
{
    public event Action? ShareButtonPressed;
    public event Action<int>? DeleteButtonPressed;

    public NewsWriteMenu(string name)
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        if (Window != null)
            Window.Title = name;

        Share.OnPressed += _ => ShareButtonPressed?.Invoke();
    }

    public void UpdateUI(NewsArticle[] articles, bool shareAvalible)
    {
        ArticleCardsContainer.Children.Clear();

        for (int i = 0; i < articles.Length; i++)
        {
            var article = articles[i];
            var mini = new MiniArticleCardControl(article.Name, (article.Author != null ? article.Author : Loc.GetString("news-read-ui-no-author")));
            mini.ArticleNum = i;
            mini.OnDeletePressed += () => DeleteButtonPressed?.Invoke(mini.ArticleNum);

            ArticleCardsContainer.AddChild(mini);
        }

        Share.Disabled = !shareAvalible;
    }
}
