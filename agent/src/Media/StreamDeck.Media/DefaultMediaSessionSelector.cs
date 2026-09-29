namespace StreamDeck.Media;

public sealed class DefaultMediaSessionSelector : IMediaSessionSelector
{
    public IWindowsMediaSession? Select(IWindowsMediaSession? currentSession, IReadOnlyList<IWindowsMediaSession> sessions)
    {
        if (currentSession is not null)
        {
            return currentSession;
        }

        return sessions.FirstOrDefault(session => session.GetPlaybackInfo().PlaybackState == MediaPlaybackState.Playing)
            ?? sessions.FirstOrDefault();
    }
}
