namespace UsersManager.Exceptions;

[Serializable]
internal class DataException //: AppException
{
    #region Properties

    public string Code => "DataException";
    public string Member { get; }
    public string Error { get; }

    #endregion

    #region Constructor

    public DataException(string member, string error) //: base($@"{member}:{error}")
    {
        Member = member;
        Error = error;
    }

    #endregion
}