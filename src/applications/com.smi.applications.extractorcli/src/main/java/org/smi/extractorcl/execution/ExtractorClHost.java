package org.smi.extractorcl.execution;

import org.apache.commons.cli.CommandLine;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.smi.common.options.GlobalOptions;
import org.smi.common.rabbitMq.RabbitMqAdapter;
import org.smi.extractorcl.exceptions.FileProcessingException;
import org.smi.extractorcl.fileUtils.CsvParser;
import org.smi.extractorcl.fileUtils.ExtractMessagesCsvHandler;

import java.io.File;
import java.io.FileNotFoundException;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.LinkedList;
import java.util.List;
import java.util.UUID;

/**
 * Main class for command line program that sends out the messages to start the
 * extraction of images.
 */
public class ExtractorClHost {

	private static Logger _logger = LoggerFactory.getLogger(ExtractorClHost.class);

	private GlobalOptions _options;
	private ExtractMessagesCsvHandler _csvHandler;
	private RabbitMqAdapter _adapter;
	private UUID _jobIdentifier;
	private LinkedList<Path> _filesToProcess;

	public ExtractorClHost(GlobalOptions options, CommandLine commandLineOptions, UUID jobIdentifier)
			throws IllegalArgumentException, FileNotFoundException, FileAlreadyExistsException {

		_options = options;

		File extractionRoot = new File(options.FileSystemOptions.getExtractRoot());

		if (!extractionRoot.exists())
			throw new FileNotFoundException("Could not locate the extraction root on startup (" + extractionRoot.getAbsolutePath() + ")");

		if (_jobIdentifier == new UUID(0L, 0L))
			throw new IllegalArgumentException("Job identifier cannot be the zero UUID");

		_jobIdentifier = jobIdentifier;

		_logger.debug("Setting up ExtractorClHost for job " + jobIdentifier);


		final String projectID = commandLineOptions.getOptionValue("p");
		final String extractionDir = projectID + "/" + commandLineOptions.getOptionValue("e", _jobIdentifier.toString() + "/");

		_logger.debug("projectID: " + projectID);
		_logger.debug("extractionDirectory: " + extractionDir);

		Path fullExtractionDirectory = Paths.get(extractionRoot.getAbsolutePath().toString(), extractionDir);

		if (fullExtractionDirectory.toFile().exists()) {
			_logger.error("Extraction directory already exists - please run again with an empty directory, or do not pass the -e option");
			throw new FileAlreadyExistsException(fullExtractionDirectory.toFile().getAbsolutePath());
		}

		fullExtractionDirectory.toFile().mkdirs();

		List<String> toProcessArgs = commandLineOptions.getArgList();

		if (toProcessArgs.size() == 0)
			throw new IllegalArgumentException("No files given to process");

		// Find the data files
		_filesToProcess = new LinkedList<>();

		_logger.debug("Data files to process: ");

		for (String arg : toProcessArgs) {

			Path path = Paths.get(arg);

			if (Files.notExists(path) || Files.isDirectory(path))
				throw new FileNotFoundException("Cannot find data file: " + arg);

			_filesToProcess.add(path);

			_logger.debug("\t" + path);
		}

		RabbitMqAdapter rabbitMQAdapter = new RabbitMqAdapter(options.RabbitOptions, "ExtractorCL");
		_logger.debug("Connected to RabbitMQ server version " + rabbitMQAdapter.getRabbitMqServerVersion());

		int extractionIdentifierColumn = parseExtractionIdentifierColumn(commandLineOptions.getOptionValue('c'));
		_logger.debug("extractionIdentifierColumn: " + extractionIdentifierColumn);

		_csvHandler = new ExtractMessagesCsvHandler(
				jobIdentifier,
				projectID,
				extractionDir,
				extractionIdentifierColumn,
				rabbitMQAdapter.SetupProducer(options.ExtractorClOptions.ExtractionRequestProducerOptions),
				rabbitMQAdapter.SetupProducer(options.ExtractorClOptions.ExtractionRequestInfoProducerOptions));
	}

	/**
	 * Processes the given list of files and writes the messages to the message
	 * exchanges.
	 *
	 * @throws FileProcessingException if an error occurs processing a file.
	 */
	public void process() throws FileProcessingException {

		_logger.info("Processing files (Job " + _jobIdentifier + ")");

		if (_filesToProcess.size() == 0) {

			_logger.warn("No files to process");
			return;
		}

		for (Path filePath : _filesToProcess) {

			_logger.debug("Processing file " + filePath);

			CsvParser parser = new CsvParser(filePath, _csvHandler);

			try {

				parser.parse();

			} catch (Throwable e) {

				throw new FileProcessingException(filePath, e);
			}
		}

		_csvHandler.sendMessages(_options.ExtractorClOptions.MaxIdentifiersPerMessage);
	}

	public void shutdown() {

		_logger.debug("Shutdown called for host");

		if (_adapter != null)
			_adapter.Shutdown();

		_adapter = null;
	}

	private int parseExtractionIdentifierColumn(String columnStr) throws NumberFormatException {

		if (columnStr == null) {
			_logger.warn("Extraction column not specified, using 0");
			return 0;
		}

		try {

			int columnIndex = Integer.valueOf(columnStr);

			if (columnIndex < 0)
				throw new NumberFormatException("column index must be greater than 0");

			return columnIndex;

		} catch (NumberFormatException e) {

			_logger.error("Couldn't parse '" + columnStr + "' to a valid column index");
			throw e;
		}
	}
}