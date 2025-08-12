using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedKernel.Common
{
    public class Result
    {
        public bool IsSuccess { get; }
        public string? Error { get; }

        public bool IsFailure => !IsSuccess;

        protected Result(bool isSuccess, string? error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result Success() => new Result(true, null);

        public static Result Failure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                throw new ArgumentException("Error message cannot be empty.", nameof(error));

            return new Result(false, error);
        }
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        protected Result(bool isSuccess, T? value, string? error)
            : base(isSuccess, error)
        {
            Value = value;
        }

        public static Result<T> Success(T value) => new Result<T>(true, value, null);

        public static new Result<T> Failure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                throw new ArgumentException("Error message cannot be empty.", nameof(error));

            return new Result<T>(false, default, error);
        }
    }
}
