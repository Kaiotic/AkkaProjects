using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WordCounter.Messages;

namespace WordCounter.Actors
{
    public class DirectoryCrawler : ReceiveActor
    {
        private int fileno = 0;
        private int fileProcessed = 0;
        private Stopwatch m_sw = new Stopwatch();
        private Dictionary<string, Boolean> filesProcessed = new Dictionary<string, bool>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectoryCrawler"/> class.
        /// </summary>
        public DirectoryCrawler()
        {
            Receive<DirectoryToSearchMessage>( msg => Handle( msg ) );
            Receive<CompletedFile>( msg => Handle( msg ) );
            Receive<FailureMessage>( msg => Handle( msg ) );
        }

        public void Handle( DirectoryToSearchMessage message )
        {
            filesProcessed.Clear();
            fileno = 0;
            fileProcessed = 0;
            m_sw.Start();
            EnumerateFiles( message.Directory, message.SearchPattern );
        }

        private void EnumerateDirectories( string staringdir, String searchPattern )
        {
            foreach ( var dir in Directory.GetDirectories( staringdir, "*.*", SearchOption.TopDirectoryOnly ) )
            {
                EnumerateFiles( dir, searchPattern );
            }
        }

        private void EnumerateFiles( string directory, String searchPattern )
        {
            try
            {
                foreach ( var file in Directory.GetFiles( directory, searchPattern, SearchOption.TopDirectoryOnly ) )
                {
                    var counterActor = Context.ActorOf( WordCounterActor.GetProps() );
                    counterActor.Tell( new FileToProcess( file, fileno ) );
                    fileno++;
                    filesProcessed.Add( file, false );
                    Context.Parent.Tell( new StatusMessage( "Processing file " + file ) );
                }

                EnumerateDirectories( directory, searchPattern );

            }
            catch ( Exception )
            {
                Context.Parent.Tell( new StatusMessage( string.Format( "Error getting file in directory : [{0}]", directory ) ) );
            }
        }

        public void Handle( CompletedFile message )
        {
            fileProcessed++;
            Context.Parent.Tell( message );

            filesProcessed[ message.FileName ] = true;
            if ( fileProcessed == fileno )
            {
                m_sw.Stop();
                Context.Parent.Tell( new StatusMessage( string.Format( "Processed {0} file(s) in {1} ms", fileno, m_sw.ElapsedMilliseconds ) ) );
                Context.Parent.Tell( new Done() );
                m_sw.Reset();
            }
        }

        public void Handle( FailureMessage fail )
        {
            var exception = fail.Cause;
            if ( exception is AggregateException )
            {
                var agg = (AggregateException)exception;
                exception = agg.InnerException;
                agg.Handle( exception1 => true );
            }
            Context.Parent.Tell( new StatusMessage( "Error " + fail.Child.Path + " " + exception != null ? exception.Message : "no exception object" ) );
        }
    }
}