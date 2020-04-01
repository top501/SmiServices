package org.smi.ctpanonymiser.test.messages;

import java.io.UnsupportedEncodingException;
import java.nio.file.Paths;
import java.util.UUID;

import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;
import org.smi.common.logging.SmiLogging;
import org.smi.ctpanonymiser.messages.ExtractFileMessage;
import org.smi.ctpanonymiser.messaging.CTPAnonymiserConsumer;

import com.google.gson.Gson;
import com.google.gson.JsonSyntaxException;

import junit.framework.TestCase;

public class ExtractFileMessageTest extends TestCase {

    private final static Logger log = LogManager.getLogger(ExtractFileMessageTest.class);
    private ExtractFileMessage _exMessage;
    private final String _fileSystemRoot = "dummyfilesystemroot";
    private final String _extractFileSystemRoot = "dummyextractfilesystemroot";

    protected void setUp() throws Exception {

        super.setUp();

        SmiLogging.Setup(true);

        _exMessage = new ExtractFileMessage();
        _exMessage.ExtractionJobIdentifier = UUID.randomUUID();
        _exMessage.JobSubmittedAt = "";
        _exMessage.ExtractionDirectory = "dummyProjectDir/dummyExtractionDir";
        _exMessage.DicomFilePath = "2018/01/01/ABCD/image-000001.dcm";
        _exMessage.OutputPath = "dummySeries/image-000001-an.dcm";
        _exMessage.ProjectNumber = "1234-5678";

    }

    protected void tearDown() throws Exception {

        super.tearDown();
    }

    public void testSerializeDeserialize() {
        ExtractFileMessage recvdMessage;
        CTPAnonymiserConsumer consumer = new CTPAnonymiserConsumer(null, null, null, _fileSystemRoot, _extractFileSystemRoot);

        // Get byte array version of message
        Gson _gson = new Gson();
        byte[] body = null;

        try {

            log.debug("Message as json: " + _gson.toJson(_exMessage, _exMessage.getClass()));
            body = _gson.toJson(_exMessage, _exMessage.getClass()).getBytes("UTF-8");

        } catch (UnsupportedEncodingException e) {

            log.error("Failed to convert message to bytes", e);
            fail("Failed to convert message to bytes");
        }

        try {

            // Convert bytes back into a new message

            recvdMessage = consumer.getMessageFromBytes(body, _exMessage.getClass());

            assertEquals("ProjectFolder", _exMessage.ExtractionDirectory, recvdMessage.ExtractionDirectory);
            assertEquals("DicomFilePath", _exMessage.DicomFilePath, recvdMessage.DicomFilePath);
            assertEquals("OutputPath", _exMessage.OutputPath, recvdMessage.OutputPath);
            assertEquals("ProjectNumber", _exMessage.ProjectNumber, recvdMessage.ProjectNumber);

        } catch (JsonSyntaxException | UnsupportedEncodingException e) {

            log.error("Failed to get message from bytes", e);
            fail("Failed to get message from bytes");
        }
    }

    public void testGetAbsolutePathToIdentifiableImage() {

        assertEquals(
                "Absolute path to original image",
                Paths.get(_fileSystemRoot + "/" + _exMessage.DicomFilePath).normalize().toString(),
                _exMessage.getAbsolutePathToIdentifiableImage(_fileSystemRoot));
    }

    public void testGetAbsolutePathToExtractAnonymousImageTo() {

        assertEquals(
                "Absolute path to extract anonymous image to",
                Paths
                .get(_extractFileSystemRoot + "/" + _exMessage.ExtractionDirectory + "/" + _exMessage.OutputPath)
                .normalize()
                .toString(),
                _exMessage.getExtractionOutputPath(_extractFileSystemRoot));
    }

    public void testBackslashReplacement() {

        String path = _fileSystemRoot + "/" + _exMessage.DicomFilePath.replace("\\", "/");
        assertEquals("Paths match", "dummyfilesystemroot/2018/01/01/ABCD/image-000001.dcm", path);
    }
}
