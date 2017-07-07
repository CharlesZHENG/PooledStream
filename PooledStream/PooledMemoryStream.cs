namespace PooledStream
{
    using System;
    using System.IO;
    using System.Buffers;
    public class PooledMemoryStream : Stream
    {
        ArrayPool<byte> m_Pool;
        byte[] _currentbuffer = null;
        bool _CanWrite = false;
        long _Length = 0;
        long _Position = 0;
        bool _FromPool = false;
        public PooledMemoryStream()
            : this(ArrayPool<byte>.Shared)
        {
        }
        public PooledMemoryStream(ArrayPool<byte> pool)
            : this(pool, 4096)
        {
            m_Pool = pool;
        }
        public PooledMemoryStream(ArrayPool<byte> pool, int capacity)
        {
            m_Pool = pool;
            _currentbuffer = m_Pool.Rent(capacity);
            _FromPool = true;
            _Length = 0;
            _CanWrite = true;
        }
        public PooledMemoryStream(ArrayPool<byte> pool, byte[] data, int offset, int length)
        {
            m_Pool = pool;
            _currentbuffer = data;
            _Length = length;
            _CanWrite = false;
        }
        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek => true;

        public override bool CanWrite => _CanWrite;

        public override long Length => _Length;

        public override long Position
        {
            get => _Position;
            set
            {
                _Position = value;
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int readlen = count > (int)(_Length - _Position) ? (int)(_Length - _Position) : count;
            Buffer.BlockCopy(_currentbuffer
                , (int)_Position
                , buffer, offset
                , readlen)
                ;
            _Position += readlen;
            return readlen;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long oldValue = _Position;
            switch ((int)origin)
            {
                case (int)SeekOrigin.Begin:
                    _Position = offset;
                    break;
                case (int)SeekOrigin.End:
                    _Position = _Length - offset;
                    break;
                case (int)SeekOrigin.Current:
                    _Position += offset;
                    break;
                default:
                    throw new InvalidOperationException("unknown SeekOrigin");
            }
            if(_Position < 0 || _Position > _Length)
            {
                _Position = oldValue;
                throw new IndexOutOfRangeException();
            }
            return _Position;
        }
        void ReallocateBuffer(int minimumRequired)
        {
            var tmp = m_Pool.Rent(minimumRequired);
            Buffer.BlockCopy(_currentbuffer, 0, tmp, 0, _currentbuffer.Length);
            m_Pool.Return(_currentbuffer);
            _currentbuffer = tmp;
        }
        public override void SetLength(long value)
        {
            if (!_CanWrite)
            {
                throw new NotSupportedException("stream is readonly");
            }
            if (value > int.MaxValue)
            {
                throw new IndexOutOfRangeException("overflow");
            }
            if (value < 0)
            {
                throw new IndexOutOfRangeException("underflow");
            }
            _Length = value;
            if (_currentbuffer.Length < _Length)
            {
                ReallocateBuffer((int)_Length);
            }
        }
        /// <summary>write data to stream</summary>
        /// <remarks>if stream data length is over int.MaxValue, this method throws IndexOutOfRangeException</remarks>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_CanWrite)
            {
                throw new InvalidOperationException("stream is readonly");
            }
            long endOffset = _Position + count;
            if (endOffset > int.MaxValue)
            {
                throw new IndexOutOfRangeException("overflow");
            }
            if (endOffset > _currentbuffer.Length)
            {
                ReallocateBuffer((int)(endOffset) * 2);
            }
            Buffer.BlockCopy(buffer, offset,
                _currentbuffer, (int)_Position, count);
            if (endOffset > _Length)
            {
                _Length = endOffset;
            }
            _Position = endOffset;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_currentbuffer != null && _FromPool)
            {
                m_Pool.Return(_currentbuffer);
                _currentbuffer = null;
            }
        }
        /// <summary>ensure the buffer size</summary>
        /// <remarks>capacity != stream buffer length</remarks>
        public void Reserve(int capacity)
        {
            if (capacity > _currentbuffer.Length)
            {
                ReallocateBuffer(capacity);
            }
        }

        /// <summary>Create newly allocated buffer and copy the stream data</summary>
        public byte[] ToArray()
        {
            var ret = new byte[_Length];
            Buffer.BlockCopy(_currentbuffer, 0, ret, 0, (int)_Length);
            return ret;
        }
        /// <summary>Create ArraySegment for current stream data without allocation buffer</summary>
        /// <remarks>after disposing stream, manupilating buffer(read or write) may cause undefined behavior</remarks>
        public ArraySegment<byte> ToUnsafeArraySegment()
        {
            return new ArraySegment<byte>(_currentbuffer, 0, (int)_Length);
        }
    }
}